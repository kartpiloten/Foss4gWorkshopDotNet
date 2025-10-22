This file contains instructions for using GitHub Copilot effectively. 
It provides guidelines on how to write prompts, examples of good and bad prompts, and tips for refining code suggestions.
# Code style and conventions
- Simplify code as much as possible. Focus on the learning experience.
- Do not include to much error handling, logging, or configuration code that distracts from the main point.
- Do not include any emojis in the code or comments.
- Make small comments in the code where .NET-specific concepts are used that may not be familiar to all users. Ex Dependency Injection, Async/Await, LINQ, etc.
- Make small comments in the code where the Nuget packages are used that may not be familiar to all users. E.g Npgsql, NetTopologySuite, etc.
- Make snmall comments in the code where FOSS4G-specific concepts are used that may not be familiar to all users. E.g PostGIS, GeoJSON, OpenLayers, Leaflet, etc.
- Make a small header in every file describing the purpose of the file. Start with the line  "The functionallity in this file is:"

# GitHub Copilot Instructions
This solution include a number of projects that will be used in a workshop for people that want to use the FOSS4G technical stack
in an .NET environment.
