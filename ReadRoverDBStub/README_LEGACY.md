# ReadRoverDBStub - LEGACY PROJECT

?? **This project has been split into two new projects:**

## New Architecture (Use These Instead)

### ?? ReadRoverDBStubLibrary
- **Purpose**: Silent, reusable rover data reading library
- **Location**: `../ReadRoverDBStubLibrary/`
- **Features**: PostgreSQL & GeoPackage support, event-driven monitoring, no console output
- **Used By**: ConvertWinddataToPolygon, FrontendVersion2

### ?? ReadRoverDBStubTester  
- **Purpose**: Console test client for debugging and diagnostics
- **Location**: `../ReadRoverDBStubTester/`
- **Features**: Verbose logging, connection diagnostics, real-time monitoring display

## Migration

If you're using this old project:

1. **For Library Usage**: Reference `ReadRoverDBStubLibrary` instead
2. **For Testing**: Use `ReadRoverDBStubTester` instead
3. **Namespace Change**: `using ReadRoverDBStubLibrary;`

## Architecture Benefits

- ? **Silent Library**: No unwanted console output
- ? **Reusable**: Used by multiple projects
- ? **Event-Driven**: Clean notification system
- ? **Separation of Concerns**: Library vs. test client

See `READROVERDBSTUB_ARCHITECTURE.md` for complete documentation.

---

**?? This legacy project will be removed in future versions. Please migrate to the new architecture.**