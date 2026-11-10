namespace BasketElo.Web.AgentReadiness;

public static class AgentApi
{
    public static AgentApiStatus Status { get; } = new(
        "ok",
        "BasketElo Agent API",
        "/openapi.json",
        "/");

    public const string OpenApiDocument = """
        {
          "openapi": "3.1.0",
          "info": {
            "title": "BasketElo Agent API",
            "version": "1.0.0",
            "description": "Machine-readable discovery and availability endpoints for BasketElo. Public basketball content is available as server-rendered HTML and as Markdown from the homepage through HTTP content negotiation."
          },
          "servers": [
            { "url": "/", "description": "Current BasketElo origin" }
          ],
          "paths": {
            "/api/v1/status": {
              "get": {
                "operationId": "getAgentApiStatus",
                "summary": "Describe the public machine-readable surface",
                "responses": {
                  "200": {
                    "description": "The service is available.",
                    "content": {
                      "application/json": {
                        "schema": { "$ref": "#/components/schemas/AgentApiStatus" }
                      }
                    }
                  },
                  "500": { "$ref": "#/components/responses/Error" }
                }
              }
            },
            "/health": {
              "get": {
                "operationId": "getHealth",
                "summary": "Check web service health",
                "responses": {
                  "200": {
                    "description": "The web service is healthy.",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "required": ["status"],
                          "properties": { "status": { "type": "string", "const": "ok" } }
                        }
                      }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "AgentApiStatus": {
                "type": "object",
                "required": ["status", "name", "openapi", "markdownHomepage"],
                "properties": {
                  "status": { "type": "string", "const": "ok" },
                  "name": { "type": "string" },
                  "openapi": { "type": "string", "format": "uri-reference" },
                  "markdownHomepage": { "type": "string", "format": "uri-reference" }
                }
              },
              "Error": {
                "type": "object",
                "required": ["type", "title", "status", "code", "message", "resolutionHint", "traceId"],
                "properties": {
                  "type": { "type": "string", "format": "uri-reference" },
                  "title": { "type": "string" },
                  "status": { "type": "integer", "minimum": 400, "maximum": 599 },
                  "detail": { "type": "string" },
                  "code": { "type": "string" },
                  "message": { "type": "string" },
                  "resolutionHint": { "type": "string" },
                  "traceId": { "type": "string" }
                }
              }
            },
            "responses": {
              "Error": {
                "description": "A structured machine-readable error.",
                "content": {
                  "application/problem+json": {
                    "schema": { "$ref": "#/components/schemas/Error" }
                  }
                }
              }
            }
          }
        }
        """;
}

public sealed record AgentApiStatus(
    string Status,
    string Name,
    string Openapi,
    string MarkdownHomepage);
