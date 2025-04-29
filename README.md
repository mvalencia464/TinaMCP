# TinaCMS MCP Server (C#)

This project implements a standalone Model Context Protocol (MCP) server using the official C# SDK (`modelcontextprotocol/csharp-sdk`). Its purpose is to expose tools that allow interaction with content files managed by a TinaCMS project repository.

This enables AI models or other MCP clients to list, read, create, update, and delete content files within a TinaCMS site via the standardized MCP.

## Features

*   Connects to a local TinaCMS project directory.
*   Uses standard MCP Stdio transport for communication.
*   Provides tools for basic content management operations:
    *   Listing collections (directories under `/content`).
    *   Listing documents within a collection (non-recursive and recursive).
    *   Reading document content.
    *   Reading document frontmatter (metadata).
    *   Creating new documents.
    *   Updating existing documents (full content replace).
    *   Updating document frontmatter (metadata merge).
    *   Deleting documents.
    *   Moving/Renaming documents.
    *   Copying documents.
    *   Reading the TinaCMS generated schema (`.tina/schema.json`).
*   Includes basic path validation and security checks.

## Prerequisites

*   **.NET 8 SDK** (or later): Required to build and run the C# server.
*   **Node.js and npm/npx:** Required to run the MCP Inspector tool for testing.
*   **A TinaCMS Project:** You need a local TinaCMS project whose content you want this server to manage.

## Setup

1.  **Clone/Download:** Get the source code for this project.
2.  **Configure Root Path:**
    *   Open the `appsettings.json` file.
    *   Locate the `TinaProject` section.
    *   **IMPORTANT:** Replace the *sample path* provided for `RootPath` with the **full, absolute path** of your target TinaCMS project's root directory (the one containing the `.tina` folder).
    *   Use forward slashes (`/`) for the path separators (recommended for cross-platform compatibility) or double backslashes (`\\`) if preferred on Windows.
    ```json
    {
      "Logging": { ... },
      "TinaProject": {
        "RootPath": "/path/to/your/tina-project" 
      }
    }
    ```
3.  **Restore Dependencies:** Open a terminal in the `TinaMcpServer` project directory and run:
    ```bash
    dotnet restore
    ```

## Development

*   **Build:** To compile the project:
    ```bash
    dotnet build
    ```
*   **Run Locally (for testing):** The server is designed to be run by an MCP client (like the Inspector). Running it directly isn't very useful, but you can start it with:
    ```bash
    dotnet run 
    ```
    or by running the compiled DLL:
    ```bash
    dotnet bin/Debug/net8.0/TinaMcpServer.dll
    ```
    The server will start and wait for an MCP client to connect via standard input/output. It will likely exit immediately or show JSON errors if run without a client.

## Testing with MCP Inspector

The recommended way to test the server and its tools is using the official MCP Inspector.

1.  **Ensure the project is built:** Run `dotnet build` if you haven't already.
2.  **Run the Inspector:** In your terminal (in the `TinaMcpServer` project directory), run the following command. This tells the Inspector to launch your compiled server DLL using `dotnet`:
    ```bash
    npx @modelcontextprotocol/inspector dotnet bin/Debug/net8.0/TinaMcpServer.dll
    ```
3.  **Open Inspector UI:** The command will output a URL (e.g., `http://127.0.0.1:6274`). Open this URL in your web browser.
4.  **Connect:** Click the "Connect" button for the detected `TinaMcpServer`.
5.  **Use Tools:**
    *   Navigate to the "Tools" tab.
    *   Click "List Tools" to see all available tools.
    *   Click on a specific tool to see its description and arguments.
    *   Fill in the required arguments and click "Run Tool".
    *   Observe the results and check the terminal running the Inspector for logs from the C# server.

## Available Tools

*(Descriptions are abbreviated here; see tool attributes in code for full details)*

*   `ListTinaCollections()`: Lists collection directories under `/content`.
*   `ListCollectionDocuments(collection)`: Lists files directly within a collection directory.
*   `ListDocumentsRecursive(collection)`: Lists all files within a collection, including subdirectories.
*   `GetTinaDocument(collection, relativePath)`: Gets the full text content of a document.
*   `CreateTinaDocument(collection, relativePath, content)`: Creates a new document; fails if it exists.
*   `UpdateTinaDocument(collection, relativePath, content)`: Overwrites an existing document; fails if it doesn't exist.
*   `DeleteTinaDocument(collection, relativePath)`: Deletes an existing document; fails if it doesn't exist.
*   `MoveDocument(collection, oldRelativePath, newRelativePath)`: Moves/renames a document.
*   `CopyDocument(collection, sourceRelativePath, destinationRelativePath)`: Copies a document.
*   `GetDocumentMetadata(collection, relativePath)`: Gets YAML frontmatter as a JSON string.
*   `UpdateDocumentMetadata(collection, relativePath, metadataUpdatesJson)`: Adds/updates frontmatter fields from a JSON object.
*   `GetCollectionSchemaInfo(collection)`: Reads the `.tina/schema.json` file content.

## Potential Future Enhancements

*   Configuration for the assumed `/content` directory name.
*   More granular update operations (e.g., partial updates instead of full replace).
*   Support for different frontmatter formats (e.g., JSON, TOML) if needed.
*   More robust schema parsing/interaction.
*   Different transport options (e.g., HTTP/SSE). 