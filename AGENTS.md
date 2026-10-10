# Rules

- Before you change a file, check the relevant documentation. You must use Context7 first for any library, framework, SDK, API, CLI tool, or cloud service. If Context7 is unavailable or lacks relevant documentation, use web search.

- Add doc on functions, structures, enums, modules, traits, etc. Add comments on complex / important logic. All docs must follow `humanizer` skill and `simplified-technical-english-asd-ste100` (when appropriate) skill.

- When implementing functions, first check if similar functions already existed somewhere. If appropriate, extract the functions and reuse them.

- Run format and test after code changes.

- Regenerate frontend api client (`openapi-generator-cli generate -i http://localhost:5119/openapi/v1.json -g typescript-fetch -o ./src/backend/openapi`) from the backend OpenAPI document after any API contract changes.
