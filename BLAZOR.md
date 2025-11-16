# Blazor: WebAssembly vs Server

## Overview

**Blazor WebAssembly (WASM)** runs your entire .NET application in the browser using WebAssembly. The app is downloaded once and executes locally, offering offline capabilities and reduced server load. It's ideal for client-heavy applications but requires larger initial downloads.

**Blazor Server** keeps your .NET application on the server and streams UI updates to the browser via SignalR/WebSocket. The browser only renders the UI, making it lightweight and fast to load. It's better for real-time collaboration and server-side resource access, but depends on a constant connection.

## Architecture Diagrams

```mermaid
graph TB
    subgraph "Blazor WebAssembly"
        D[Client Browser<br>.NET Runtime + App Logic] -->|"Direct Execution"| E[UI Rendering<br>in Browser]
        D -->|"HTTP Calls"| F["Backend API<br> (Optional for Data)"]
    end

    style D fill:#bbf,stroke:#333,color:#000
    style E fill:#f9f,stroke:#333,color:#000
```

```mermaid
graph TB
    subgraph "Blazor Server"
        A[Client Browser<br>Thin UI Layer] -->|"SignalR (WebSocket)"| B[Server<br>.NET Runtime + App Logic]
        B -->|"UI Updates"| A
        B -->|"Direct Access"| C["Server Resources<br> (DB, Files, etc.)"]
    end

    style A fill:#f9f,stroke:#333,color:#000
    style B fill:#bbf,stroke:#333,color:#000
```