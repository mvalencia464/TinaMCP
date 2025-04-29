using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Text;
using System.Text.Json; // For parsing JSON metadata updates
using System.Text.RegularExpressions; // For frontmatter regex
using YamlDotNet.Serialization; // For YAML parsing/serialization
using YamlDotNet.Serialization.NamingConventions;

namespace TinaMcpServer;

[McpServerToolType]
public class TinaCmsContentTools
{
    private readonly TinaProjectConfig _config;
    private readonly ILogger<TinaCmsContentTools> _logger;
    private readonly string _assumedContentRoot;

    public TinaCmsContentTools(TinaProjectConfig config, ILogger<TinaCmsContentTools> logger)
    {
        _config = config;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_config.RootPath))
        {
            _logger.LogError("TinaCMS RootPath is not configured in appsettings.json. Tools will not function.");
            _assumedContentRoot = string.Empty; // Prevent NullReferenceException later
            // Consider throwing a configuration exception during startup for critical missing config
        }
        else if (!Directory.Exists(_config.RootPath))
        {
             _logger.LogError("Configured TinaCMS RootPath does not exist: {Path}. Tools will not function.", _config.RootPath);
             _assumedContentRoot = string.Empty;
        }
        else
        {
            // Assume content lives in a 'content' subdirectory. This might need configuration later.
            _assumedContentRoot = Path.GetFullPath(Path.Combine(_config.RootPath, "content"));
             _logger.LogInformation("Assuming TinaCMS content root is: {ContentRoot}", _assumedContentRoot);
             if (!Directory.Exists(_assumedContentRoot))
             {
                 _logger.LogWarning("Assumed content directory does not exist: {ContentRoot}. Tools requiring it may fail.", _assumedContentRoot);
             }
        }
    }

    [McpServerTool, Description("Reads a specific document from a TinaCMS collection assuming standard paths (e.g., {RootPath}/content/{collection}/{relativePath}).")]
    public async Task<string> GetTinaDocument(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path of the document within the collection (e.g., 'hello-world.md')")] string relativePath)
    {
        _logger.LogInformation("GetTinaDocument called for {Collection}/{RelativePath}", collection, relativePath);

        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
             _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot get document.");
             throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }

        // Basic path validation to prevent directory traversal
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains("..") ||
            string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains(".."))
        {
            _logger.LogError("Invalid collection name or relative path provided: Collection='{Collection}', RelativePath='{RelativePath}'", collection, relativePath);
            throw new ArgumentException("Invalid collection name or relativePath provided (contains invalid characters or path traversal).");
        }

        try
        {
            string collectionPath = Path.Combine(_assumedContentRoot, collection);
            string fullPath = Path.Combine(collectionPath, relativePath);

            // Security check: Ensure the final path is still within the intended content root
             if (!Path.GetFullPath(fullPath).StartsWith(_assumedContentRoot, StringComparison.OrdinalIgnoreCase))
             {
                 _logger.LogError("Path traversal attempt detected or path outside content root: {FullPath}", fullPath);
                 throw new ArgumentException("Calculated path is outside the allowed content directory.");
             }

            _logger.LogDebug("Attempting to read file: {FullPath}", fullPath);

            if (!File.Exists(fullPath))
            {
                 _logger.LogWarning("File not found: {FullPath}", fullPath);
                 throw new FileNotFoundException($"Document '{relativePath}' not found in collection '{collection}'.", fullPath);
            }

            string content = await File.ReadAllTextAsync(fullPath);
            _logger.LogInformation("Successfully read document: {FullPath}", fullPath);
            return content;
        }
        catch (ArgumentException) 
        {
             throw; 
        }
        catch (FileNotFoundException ex) 
        {
             _logger.LogWarning(ex, "File not found exception for {Collection}/{RelativePath}", collection, relativePath);
             throw; 
        }
        catch (Exception ex) 
        {
             _logger.LogError(ex, "Error reading document {Collection}/{RelativePath}", collection, relativePath);
             throw new Exception($"An error occurred while trying to read document '{relativePath}' in collection '{collection}'. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Lists assumed collection directories within the configured content folder ({RootPath}/content/).")]
    public Task<List<string>> ListTinaCollections()
    {
        _logger.LogInformation("ListTinaCollections called.");

        if (string.IsNullOrWhiteSpace(_assumedContentRoot) || !Directory.Exists(_assumedContentRoot))
        {
            _logger.LogWarning("Assumed content directory is not configured or does not exist: {ContentRoot}. Returning empty list.", _assumedContentRoot);
            return Task.FromResult(new List<string>()); 
        }

        try
        {
            _logger.LogDebug("Listing collections (subdirectories) in: {ContentDir}", _assumedContentRoot);

            var directories = Directory.GetDirectories(_assumedContentRoot)
                                      .Select(Path.GetFileName) 
                                      .Where(name => !string.IsNullOrEmpty(name) && !name.StartsWith(".")) // Filter out nulls/empty and hidden dirs
                                      .Select(name => name!) 
                                      .ToList();

            _logger.LogInformation("Found {Count} potential collections: {Collections}", directories.Count, string.Join(", ", directories));
            return Task.FromResult(directories); 
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error listing collections in: {ContentDir}", _assumedContentRoot);
             throw new Exception($"An error occurred while trying to list collections. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Lists documents within a specific collection directory ({RootPath}/content/{collection}).")]
    public Task<List<string>> ListCollectionDocuments(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection)
    {
        _logger.LogInformation("ListCollectionDocuments called for {Collection}", collection);

        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
            _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot list documents.");
            throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }

        // Basic path validation
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains(".."))
        {
            _logger.LogError("Invalid collection name provided: {Collection}", collection);
            throw new ArgumentException("Invalid collection name provided (contains invalid characters or path traversal).");
        }

        try
        {
            string collectionPath = Path.Combine(_assumedContentRoot, collection);
            _logger.LogDebug("Listing documents in: {CollectionPath}", collectionPath);

            if (!Directory.Exists(collectionPath))
            {
                _logger.LogWarning("Collection directory not found: {CollectionPath}", collectionPath);
                return Task.FromResult(new List<string>()); 
            }

            // Use EnumerateFiles to get files (non-recursively for now)
            // Use Path.GetFileName to return only the file name/relative path within the collection
            var documents = Directory.EnumerateFiles(collectionPath, "*", SearchOption.TopDirectoryOnly) 
                                     .Select(Path.GetFileName)
                                     .Where(fileName => !string.IsNullOrEmpty(fileName)) 
                                     .Select(fileName => fileName!) 
                                     .ToList();

            _logger.LogInformation("Found {Count} documents in collection '{Collection}': {Documents}", documents.Count, collection, string.Join(", ", documents));
            return Task.FromResult(documents);
        }
        catch (ArgumentException ex) 
        {
            _logger.LogError(ex, "Argument error listing documents for collection {Collection}", collection);
            throw; 
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents for collection {Collection}", collection);
            throw new Exception($"An error occurred while trying to list documents for collection '{collection}'. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Recursively lists all documents within a specific collection directory and its subdirectories ({RootPath}/content/{collection}).")]
    public Task<List<string>> ListDocumentsRecursive(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection)
    {
        _logger.LogInformation("ListDocumentsRecursive called for {Collection}", collection);

        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
            _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot list documents.");
            throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }

        // Basic path validation
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains(".."))
        {
            _logger.LogError("Invalid collection name provided: {Collection}", collection);
            throw new ArgumentException("Invalid collection name provided (contains invalid characters or path traversal).");
        }

        try
        {
            string collectionPath = Path.Combine(_assumedContentRoot, collection);
            _logger.LogDebug("Recursively listing documents in: {CollectionPath}", collectionPath);

            if (!Directory.Exists(collectionPath))
            {
                _logger.LogWarning("Collection directory not found: {CollectionPath}", collectionPath);
                return Task.FromResult(new List<string>()); 
            }

            // Use EnumerateFiles with AllDirectories
            var documents = Directory.EnumerateFiles(collectionPath, "*", SearchOption.AllDirectories)
                                     .Select(fullPath => Path.GetRelativePath(collectionPath, fullPath))
                                     // Normalize directory separators for consistency
                                     .Select(relativePath => relativePath.Replace(Path.DirectorySeparatorChar, '/'))
                                     .Where(relativePath => !string.IsNullOrEmpty(relativePath)) 
                                     .Select(relativePath => relativePath!) 
                                     .ToList();

            _logger.LogInformation("Found {Count} documents recursively in collection '{Collection}'", documents.Count, collection);
            return Task.FromResult(documents);
        }
        catch (ArgumentException ex) 
        { _logger.LogError(ex, "Argument error listing documents recursively for collection {Collection}", collection); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents recursively for collection {Collection}", collection);
            throw new Exception($"An error occurred while trying to list documents recursively for collection '{collection}'. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Creates a new document with the provided content in a specified collection ({RootPath}/content/{collection}/{relativePath}). Fails if the document already exists.")]
    public async Task<string> CreateTinaDocument(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path for the new document within the collection (e.g., 'new-post.md')")] string relativePath,
        [Description("The full text content for the new document.")] string content)
    {
        _logger.LogInformation("CreateTinaDocument called for {Collection}/{RelativePath}", collection, relativePath);

        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
            _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot create document.");
            throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }

        // Path validation
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains("..") ||
            string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..") || relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
        {
            _logger.LogError("Invalid collection name or relative path provided for creation: Collection='{Collection}', RelativePath='{RelativePath}'", collection, relativePath);
            throw new ArgumentException("Invalid collection name or relativePath provided (contains invalid characters or path traversal).");
        }

        try
        {
            string collectionPath = Path.Combine(_assumedContentRoot, collection);
            string fullPath = Path.Combine(collectionPath, relativePath);

            // Security check
             if (!Path.GetFullPath(fullPath).StartsWith(_assumedContentRoot, StringComparison.OrdinalIgnoreCase))
             {
                 _logger.LogError("Path traversal attempt detected or path outside content root during create: {FullPath}", fullPath);
                 throw new ArgumentException("Calculated path is outside the allowed content directory.");
             }

            _logger.LogDebug("Attempting to create file at: {FullPath}", fullPath);

            // Ensure collection directory exists
            Directory.CreateDirectory(collectionPath); // Creates if not exists, does nothing if exists

            // Check if file already exists
            if (File.Exists(fullPath))
            {
                _logger.LogWarning("File already exists, cannot create: {FullPath}", fullPath);
                throw new IOException($"Document '{relativePath}' already exists in collection '{collection}'. Use UpdateTinaDocument to modify.");
            }

            // Write the new file content
            await File.WriteAllTextAsync(fullPath, content);
            _logger.LogInformation("Successfully created document: {FullPath}", fullPath);
            return $"Document '{relativePath}' created successfully in collection '{collection}'.";
        }
        catch (ArgumentException ex)
        {
            throw; 
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error creating document {Collection}/{RelativePath}", collection, relativePath);
            throw; // Re-throw specific IO exception (e.g., file exists)
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Generic error creating document {Collection}/{RelativePath}", collection, relativePath);
             throw new Exception($"An error occurred while trying to create document '{relativePath}' in collection '{collection}'. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Updates an existing document with the provided content ({RootPath}/content/{collection}/{relativePath}). Fails if the document does not exist.")]
    public async Task<string> UpdateTinaDocument(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path of the existing document within the collection (e.g., 'hello-world.md')")] string relativePath,
        [Description("The new full text content for the document.")] string content)
    {
        _logger.LogInformation("UpdateTinaDocument called for {Collection}/{RelativePath}", collection, relativePath);
        
        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
            _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot update document.");
            throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }

        // Path validation
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains("..") ||
            string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..") || relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
        {
            _logger.LogError("Invalid collection name or relative path provided for update: Collection='{Collection}', RelativePath='{RelativePath}'", collection, relativePath);
            throw new ArgumentException("Invalid collection name or relativePath provided (contains invalid characters or path traversal).");
        }

        try
        {
            string collectionPath = Path.Combine(_assumedContentRoot, collection);
            string fullPath = Path.Combine(collectionPath, relativePath);

            // Security check
             if (!Path.GetFullPath(fullPath).StartsWith(_assumedContentRoot, StringComparison.OrdinalIgnoreCase))
             {
                 _logger.LogError("Path traversal attempt detected or path outside content root during update: {FullPath}", fullPath);
                 throw new ArgumentException("Calculated path is outside the allowed content directory.");
             }

            _logger.LogDebug("Attempting to update file at: {FullPath}", fullPath);

            // Check if file exists before updating
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File not found, cannot update: {FullPath}", fullPath);
                throw new FileNotFoundException($"Document '{relativePath}' does not exist in collection '{collection}'. Cannot update.", fullPath);
            }

            // Overwrite the file content
            await File.WriteAllTextAsync(fullPath, content);
            _logger.LogInformation("Successfully updated document: {FullPath}", fullPath);
            return $"Document '{relativePath}' updated successfully in collection '{collection}'.";
        }
        catch (ArgumentException ex)
        {
            throw; 
        }
         catch (FileNotFoundException ex)
        {
             _logger.LogWarning(ex, "File not found exception during update for {Collection}/{RelativePath}", collection, relativePath);
             throw; // Re-throw
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error updating document {Collection}/{RelativePath}", collection, relativePath);
            throw; // Re-throw specific IO exception
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Generic error updating document {Collection}/{RelativePath}", collection, relativePath);
             throw new Exception($"An error occurred while trying to update document '{relativePath}' in collection '{collection}'. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Deletes an existing document ({RootPath}/content/{collection}/{relativePath}). Fails if the document does not exist.")]
    public Task<string> DeleteTinaDocument(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path of the document to delete within the collection (e.g., 'old-post.md')")] string relativePath)
    {
         _logger.LogInformation("DeleteTinaDocument called for {Collection}/{RelativePath}", collection, relativePath);

        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
            _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot delete document.");
            throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }

        // Path validation
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains("..") ||
            string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..") || relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
        {
            _logger.LogError("Invalid collection name or relative path provided for deletion: Collection='{Collection}', RelativePath='{RelativePath}'", collection, relativePath);
            throw new ArgumentException("Invalid collection name or relativePath provided (contains invalid characters or path traversal).");
        }

        try
        {
            string collectionPath = Path.Combine(_assumedContentRoot, collection);
            string fullPath = Path.Combine(collectionPath, relativePath);

            // Security check
             if (!Path.GetFullPath(fullPath).StartsWith(_assumedContentRoot, StringComparison.OrdinalIgnoreCase))
             {
                 _logger.LogError("Path traversal attempt detected or path outside content root during delete: {FullPath}", fullPath);
                 throw new ArgumentException("Calculated path is outside the allowed content directory.");
             }

            _logger.LogDebug("Attempting to delete file at: {FullPath}", fullPath);

            // Check if file exists before deleting
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File not found, cannot delete: {FullPath}", fullPath);
                throw new FileNotFoundException($"Document '{relativePath}' does not exist in collection '{collection}'. Cannot delete.", fullPath);
            }

            // Delete the file (Synchronous)
            File.Delete(fullPath); 
            _logger.LogInformation("Successfully deleted document: {FullPath}", fullPath);
            // Ensure Task.FromResult is used for non-async method returning Task<string>
            return Task.FromResult($"Document '{relativePath}' deleted successfully from collection '{collection}'."); 
        }
        catch (ArgumentException ex)
        {
            throw; 
        }
         catch (FileNotFoundException ex)
        {
             _logger.LogWarning(ex, "File not found exception during delete for {Collection}/{RelativePath}", collection, relativePath);
             throw; // Re-throw
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error deleting document {Collection}/{RelativePath}", collection, relativePath);
            throw; // Re-throw specific IO exception
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Generic error deleting document {Collection}/{RelativePath}", collection, relativePath);
             throw new Exception($"An error occurred while trying to delete document '{relativePath}' in collection '{collection}'. Error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Moves/renames a document within a collection ({RootPath}/content/{collection}). Ensures the destination is within the content root.")]
    public Task<string> MoveDocument(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The current relative path of the document (e.g., 'old-name.md')")] string oldRelativePath,
        [Description("The new relative path for the document (e.g., 'new-name.md' or 'subdir/new-name.md')")] string newRelativePath)
    {
        _logger.LogInformation("MoveDocument called for {Collection}: '{OldPath}' -> '{NewPath}'", collection, oldRelativePath, newRelativePath);
        
        string collectionPath = ValidateAndGetCollectionPath(collection, "move document");
        string oldFullPath = ValidateAndGetFullPath(collectionPath, oldRelativePath, "oldRelativePath", allowSubdirs: true);
        string newFullPath = ValidateAndGetFullPath(collectionPath, newRelativePath, "newRelativePath", allowSubdirs: true);

        try
        {
            _logger.LogDebug("Attempting to move file from {OldFullPath} to {NewFullPath}", oldFullPath, newFullPath);

            if (!File.Exists(oldFullPath))
            {
                _logger.LogWarning("Source file not found, cannot move: {OldFullPath}", oldFullPath);
                throw new FileNotFoundException($"Source document '{oldRelativePath}' does not exist in collection '{collection}'. Cannot move.", oldFullPath);
            }

            if (File.Exists(newFullPath))
            {
                 _logger.LogWarning("Destination file already exists, cannot move: {NewFullPath}", newFullPath);
                 throw new IOException($"Destination document '{newRelativePath}' already exists in collection '{collection}'. Cannot move.");
            }

            // Ensure destination directory exists
            string? destDir = Path.GetDirectoryName(newFullPath);
            if (!string.IsNullOrEmpty(destDir)) {
                 Directory.CreateDirectory(destDir);
            }

            File.Move(oldFullPath, newFullPath);
            _logger.LogInformation("Successfully moved document from {OldFullPath} to {NewFullPath}", oldFullPath, newFullPath);
            return Task.FromResult($"Document moved successfully from '{oldRelativePath}' to '{newRelativePath}' in collection '{collection}'.");
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FileNotFoundException || ex is IOException)
        { _logger.LogError(ex, "Validation or IO error moving document {Collection}: '{OldPath}' -> '{NewPath}'", collection, oldRelativePath, newRelativePath); throw; }
    }

     [McpServerTool, Description("Copies a document within a collection ({RootPath}/content/{collection}). Ensures the destination is within the content root.")]
    public Task<string> CopyDocument(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path of the source document (e.g., 'template.md')")] string sourceRelativePath,
        [Description("The relative path for the new copy (e.g., 'new-doc.md' or 'subdir/new-doc.md')")] string destinationRelativePath)
    {
        _logger.LogInformation("CopyDocument called for {Collection}: '{SourcePath}' -> '{DestPath}'", collection, sourceRelativePath, destinationRelativePath);
        
        string collectionPath = ValidateAndGetCollectionPath(collection, "copy document");
        string sourceFullPath = ValidateAndGetFullPath(collectionPath, sourceRelativePath, "sourceRelativePath", allowSubdirs: true);
        string destFullPath = ValidateAndGetFullPath(collectionPath, destinationRelativePath, "destinationRelativePath", allowSubdirs: true);

        try
        {
             _logger.LogDebug("Attempting to copy file from {SourceFullPath} to {DestFullPath}", sourceFullPath, destFullPath);

            if (!File.Exists(sourceFullPath))
            {
                _logger.LogWarning("Source file not found, cannot copy: {SourceFullPath}", sourceFullPath);
                throw new FileNotFoundException($"Source document '{sourceRelativePath}' does not exist in collection '{collection}'. Cannot copy.", sourceFullPath);
            }

            if (File.Exists(destFullPath))
            {
                 _logger.LogWarning("Destination file already exists, cannot copy: {DestFullPath}", destFullPath);
                 throw new IOException($"Destination document '{destinationRelativePath}' already exists in collection '{collection}'. Cannot copy.");
            }

            // Ensure destination directory exists
            string? destDir = Path.GetDirectoryName(destFullPath);
            if (!string.IsNullOrEmpty(destDir)) {
                 Directory.CreateDirectory(destDir);
            }

            File.Copy(sourceFullPath, destFullPath);
            _logger.LogInformation("Successfully copied document from {SourceFullPath} to {DestFullPath}", sourceFullPath, destFullPath);
            return Task.FromResult($"Document copied successfully from '{sourceRelativePath}' to '{destinationRelativePath}' in collection '{collection}'.");
        }
         catch (Exception ex) when (ex is ArgumentException || ex is FileNotFoundException || ex is IOException)
        { _logger.LogError(ex, "Validation or IO error copying document {Collection}: '{SourcePath}' -> '{DestPath}'", collection, sourceRelativePath, destinationRelativePath); throw; }
    }

    [McpServerTool, Description("Gets the YAML frontmatter (metadata) from a document ({RootPath}/content/{collection}/{relativePath}) as a JSON string.")]
    public async Task<string> GetDocumentMetadata(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path of the document (e.g., 'hello-world.md')")] string relativePath)
    {
        _logger.LogInformation("GetDocumentMetadata called for {Collection}/{RelativePath}", collection, relativePath);
        
        string collectionPath = ValidateAndGetCollectionPath(collection, "get metadata");
        string fullPath = ValidateAndGetFullPath(collectionPath, relativePath, "relativePath", allowSubdirs: true);

        try
        {
             _logger.LogDebug("Attempting to read metadata from: {FullPath}", fullPath);
             if (!File.Exists(fullPath))
             {
                 _logger.LogWarning("File not found for metadata: {FullPath}", fullPath);
                 throw new FileNotFoundException($"Document '{relativePath}' not found in collection '{collection}'.", fullPath);
             }

            string fileContent = await File.ReadAllTextAsync(fullPath);
            var (metadata, _) = ExtractFrontmatter(fileContent);

            if (metadata == null)
            {
                 _logger.LogInformation("No YAML frontmatter found in {FullPath}", fullPath);
                 return "{}"; // Return empty JSON object if no frontmatter
            }

             // Convert YAML object to JSON string for return
            var jsonString = JsonSerializer.Serialize(metadata);
            _logger.LogInformation("Successfully extracted metadata for {FullPath}", fullPath);
            return jsonString;

        }
        catch (Exception ex) when (ex is ArgumentException || ex is FileNotFoundException || ex is YamlDotNet.Core.YamlException)
        { _logger.LogError(ex, "Validation, IO, or YAML error getting metadata for {Collection}/{RelativePath}", collection, relativePath); throw; }
    }

    [McpServerTool, Description("Updates the YAML frontmatter of a document ({RootPath}/content/{collection}/{relativePath}) using a JSON object containing the updates.")]
    public async Task<string> UpdateDocumentMetadata(
        [Description("The name of the TinaCMS collection (e.g., 'posts')")] string collection,
        [Description("The relative path of the document (e.g., 'hello-world.md')")] string relativePath,
        [Description("A JSON string representing the metadata fields to add or update (e.g., {\"draft\": false, \"title\": \"New Title\"})")] string metadataUpdatesJson)
    {
        _logger.LogInformation("UpdateDocumentMetadata called for {Collection}/{RelativePath}", collection, relativePath);

        string collectionPath = ValidateAndGetCollectionPath(collection, "update metadata");
        string fullPath = ValidateAndGetFullPath(collectionPath, relativePath, "relativePath", allowSubdirs: true);

        try
        {
             _logger.LogDebug("Attempting to update metadata for: {FullPath}", fullPath);
             if (!File.Exists(fullPath))
             {
                 _logger.LogWarning("File not found for metadata update: {FullPath}", fullPath);
                 throw new FileNotFoundException($"Document '{relativePath}' not found in collection '{collection}'.", fullPath);
             }

             // --- Read existing content and extract parts ---
             string originalContent = await File.ReadAllTextAsync(fullPath);
             var (existingMetadata, bodyContent) = ExtractFrontmatter(originalContent);
             existingMetadata ??= new Dictionary<object, object>(); // Start with empty dict if no frontmatter existed
             bodyContent ??= originalContent; // If no frontmatter, body is the whole content

            // --- Parse the updates JSON ---
            Dictionary<string, JsonElement>? updates;
            try
            {
                 updates = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadataUpdatesJson);
                 if (updates == null) throw new JsonException("Deserialized JSON update object is null.");
            }
            catch (JsonException jsonEx)
            {
                 _logger.LogError(jsonEx, "Failed to parse metadataUpdatesJson: {Json}", metadataUpdatesJson);
                 throw new ArgumentException("Invalid JSON format provided for metadataUpdatesJson.", nameof(metadataUpdatesJson), jsonEx);
            }
            
            // --- Merge updates into existing metadata ---
            // This simple merge overwrites existing keys and adds new ones.
            // It converts JSON values to basic types suitable for YAML serialization.
            foreach (var kvp in updates)
            {   
                // Convert JsonElement to simple types (string, bool, number, etc.) or handle recursively if needed
                // YamlDotNet can often handle simple types directly. Complex objects might need specific mapping.
                object? yamlValue = ConvertJsonElementToYamlCompatible(kvp.Value);
                existingMetadata[kvp.Key] = yamlValue; // Add or overwrite
            }

            // --- Serialize updated metadata back to YAML ---
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance) // Or choose another convention
                .Build();
            var updatedYamlMetadata = serializer.Serialize(existingMetadata);

            // --- Reconstruct the file content --- 
            var newFileContent = new StringBuilder();
            newFileContent.AppendLine("---");
            newFileContent.Append(updatedYamlMetadata); // Append YAML, ensuring it ends with a newline if needed
            if (!updatedYamlMetadata.EndsWith(Environment.NewLine) && !updatedYamlMetadata.EndsWith("\n") && !updatedYamlMetadata.EndsWith("---")) {
                 newFileContent.AppendLine(); // Add newline if serializer didn't
            }
             if (!updatedYamlMetadata.EndsWith("---")) { // Add ending delimiter if needed
                 newFileContent.AppendLine("---");
             }
             // Add a blank line after frontmatter if body exists, common practice
             if (!string.IsNullOrEmpty(bodyContent)) {
                newFileContent.AppendLine(); 
             }
            newFileContent.Append(bodyContent);

            // --- Write the updated content back to the file ---
            await File.WriteAllTextAsync(fullPath, newFileContent.ToString());
            _logger.LogInformation("Successfully updated metadata for document: {FullPath}", fullPath);
            return $"Metadata updated successfully for document '{relativePath}' in collection '{collection}'.";

        }
        catch (Exception ex) when (ex is ArgumentException || ex is FileNotFoundException || ex is YamlDotNet.Core.YamlException || ex is IOException || ex is JsonException)
        { _logger.LogError(ex, "Error updating metadata for {Collection}/{RelativePath}", collection, relativePath); throw; }
    }

    [McpServerTool, Description("Gets the content of the TinaCMS schema file (assumed to be {RootPath}/.tina/schema.json).")]
    public async Task<string> GetCollectionSchemaInfo(
        [Description("(Optional) Specify a collection name to potentially filter schema info in the future, currently ignored.")] string? collection = null)
    {
         _logger.LogInformation("GetCollectionSchemaInfo called for collection: {Collection}", collection ?? "(all)");

         if (string.IsNullOrWhiteSpace(_config.RootPath) || !Directory.Exists(_config.RootPath))
         {
             _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot get schema.");
             throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
         }

        // Schema is expected in .tina subfolder of the RootPath (not the content root)
        string schemaPath = Path.Combine(_config.RootPath, ".tina", "schema.json"); 
        // Alternatively, could try schema.ts if we had a TS parser, or make path configurable.

        try
        {
            _logger.LogDebug("Attempting to read schema file: {SchemaPath}", schemaPath);
            if (!File.Exists(schemaPath))
            {
                _logger.LogWarning("Schema file not found: {SchemaPath}. Ensure TinaCMS has generated schema.json.", schemaPath);
                throw new FileNotFoundException("TinaCMS schema file (expected at .tina/schema.json) not found.", schemaPath);
            }

            string schemaContent = await File.ReadAllTextAsync(schemaPath);
             _logger.LogInformation("Successfully read schema file: {SchemaPath}", schemaPath);
            return schemaContent;
        }
        catch (FileNotFoundException ex)
        { _logger.LogError(ex, "Schema file not found at {SchemaPath}", schemaPath); throw; }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error reading schema file at {SchemaPath}", schemaPath);
             throw new Exception($"An error occurred while reading the schema file. Error: {ex.Message}");
        }
    }

    // --- Helper Methods ---

    private string ValidateAndGetCollectionPath(string collection, string operation)
    {
        if (string.IsNullOrWhiteSpace(_assumedContentRoot))
        {
            _logger.LogError("TinaCMS RootPath is not configured or valid. Cannot {Operation}.", operation);
            throw new InvalidOperationException("TinaCMS RootPath is not configured or valid.");
        }
        if (string.IsNullOrWhiteSpace(collection) || collection.Contains(Path.DirectorySeparatorChar) || collection.Contains(Path.AltDirectorySeparatorChar) || collection.Contains(".."))
        {
            _logger.LogError("Invalid collection name provided for {Operation}: {Collection}", operation, collection);
            throw new ArgumentException("Invalid collection name provided (contains invalid characters or path traversal).");
        }
        return Path.Combine(_assumedContentRoot, collection);
    }

    private string ValidateAndGetFullPath(string collectionPath, string relativePath, string paramName, bool allowSubdirs = false)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..") || (!allowSubdirs && (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))))
        {
            _logger.LogError("Invalid relative path provided for {ParamName}: {RelativePath}", paramName, relativePath);
            throw new ArgumentException($"Invalid {paramName} provided (contains invalid characters or path traversal{(allowSubdirs ? "" : " or subdirectories")}).", paramName);
        }

        string fullPath = Path.Combine(collectionPath, relativePath);

        // Security check: Ensure the final path is still within the intended content root
        if (!Path.GetFullPath(fullPath).StartsWith(_assumedContentRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Path traversal attempt detected or path outside content root for {ParamName}: {FullPath}", paramName, fullPath);
            throw new ArgumentException($"Calculated path for {paramName} is outside the allowed content directory.");
        }
        return fullPath;
    }

    // Simple Regex to find YAML frontmatter. Handles --- at start and end.
    private static readonly Regex FrontmatterRegex = 
        new Regex(@"^---\s*$(.*?)^---\s*$?", RegexOptions.Singleline | RegexOptions.Multiline);

    private (Dictionary<object, object>? Metadata, string? Body) ExtractFrontmatter(string fileContent)
    {
        var match = FrontmatterRegex.Match(fileContent);
        if (!match.Success)
        {
            return (null, fileContent); // No frontmatter found
        }

        var yamlContent = match.Groups[1].Value;
        var bodyContent = fileContent.Substring(match.Length);

        // Trim leading whitespace/newlines from body often left after split
        bodyContent = bodyContent.TrimStart('\r', '\n'); 

        try
        {
             var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance) // Match common JS/JSON usage
                .Build();
            var metadata = deserializer.Deserialize<Dictionary<object, object>>(yamlContent);
            return (metadata, bodyContent);
        }
        catch (Exception ex)
        { 
            _logger.LogError(ex, "Failed to parse YAML frontmatter.");
            throw new YamlDotNet.Core.YamlException("Failed to parse YAML frontmatter.", ex);
        }
    }

    // Helper to convert JsonElement to basic types for YAML serialization
    private object? ConvertJsonElementToYamlCompatible(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:  return element.GetString();
            case JsonValueKind.Number:  return element.TryGetInt64(out long l) ? (object)l : element.GetDouble();
            case JsonValueKind.True:    return true;
            case JsonValueKind.False:   return false;
            case JsonValueKind.Null:    return null;
            case JsonValueKind.Object: 
                // Convert nested objects recursively
                var dict = new Dictionary<object, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    // Ensure the key is treated as a string for YAML compatibility if it wasn't already
                    dict[(object)property.Name] = ConvertJsonElementToYamlCompatible(property.Value); // Cast key to object
                }
                return dict;
            case JsonValueKind.Array:
                 // Convert arrays recursively
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertJsonElementToYamlCompatible(item));
                }
                return list;
            case JsonValueKind.Undefined: 
            default: return null; // Or throw an exception for unsupported types
        }
    }
} 