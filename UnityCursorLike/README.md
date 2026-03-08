# CursorLike Editor Assistant (Unity)

Unity Editor plugin that provides a Cursor-like chatbot inside the editor, powered by multiple LLM providers.

## What it does

- Chat UI directly in Unity: `Tools > CursorLike Chat`
- Sends project context (files/scripts/scene snapshot) to the model
- Parses executable action blocks from model output
- Can apply edits to files and scene objects
- Includes autonomous multi-step Agent mode (plan -> actions -> execution feedback -> next step)
- Supports multiple AI backends: OpenRouter, OpenAI-compatible APIs, Anthropic, and local Ollama
- Exposes precise scene transforms (world/local position, rotation, scale) in context

## Install

1. Copy folder `UnityCursorLike` into your Unity project `Packages/` directory.
2. Open Unity.
3. Configure provider and credentials in `Edit > Preferences > CursorLike`.
4. Open `Tools > CursorLike Chat`.

## Supported services

- `OpenRouter` (default)
- `OpenAI-compatible` (OpenAI, Groq, Together, etc. if they expose chat-completions schema)
- `Anthropic` (messages API)
- `Ollama` (local `http://localhost:11434/api/chat`)

Default endpoint by provider:

- OpenRouter: `https://openrouter.ai/api/v1/chat/completions`
- OpenAI-compatible: `https://api.openai.com/v1/chat/completions`
- Anthropic: `https://api.anthropic.com/v1/messages`
- Ollama: `http://localhost:11434/api/chat`

Notes:

- API key is required for OpenRouter/OpenAI-compatible/Anthropic.
- API key is optional for local Ollama.
- `Temperature` and `Max Tokens` are configurable in Preferences.

## Action format

When the assistant wants to modify the project, it should return:

```text
```cursor-actions
WRITE_FILE Assets/Scripts/PlayerController.cs
using UnityEngine;
public class PlayerController : MonoBehaviour {}
END_FILE

CREATE_GAMEOBJECT EnemySpawn
ADD_COMPONENT EnemySpawn UnityEngine.BoxCollider
SET_TRANSFORM EnemySpawn 0 1 0
SET_COMPONENT_FIELD EnemySpawn UnityEngine.BoxCollider isTrigger true
```
```

You can return multiple `cursor-actions` blocks in the same response. They will be parsed and executed in order.

Supported commands:

- `WRITE_FILE <path>` + `END_FILE`
- `APPEND_FILE <path>` + `END_FILE`
- `INSERT_IN_FILE <path> <line>` + `END_FILE`
- `REPLACE_IN_FILE <path>` + `SEARCH`/`END_SEARCH` + `REPLACE`/`END_REPLACE`
- `DELETE_FILE <path>`
- `MOVE_FILE <from> <to>`
- `COPY_FILE <from> <to>`
- `CREATE_DIR <path>`
- `DELETE_DIR <path>`
- `CREATE_GAMEOBJECT <name>`
- `CREATE_CHILD <parentPath> <name>`
- `DUPLICATE_GAMEOBJECT <path>`
- `RENAME_GAMEOBJECT <path> <newName>`
- `SET_TRANSFORM <path> <x> <y> <z>`
- `SET_LOCAL_TRANSFORM <path> <x> <y> <z>`
- `SET_ROTATION <path> <x> <y> <z>`
- `SET_LOCAL_ROTATION <path> <x> <y> <z>`
- `SET_SCALE <path> <x> <y> <z>`
- `SET_ACTIVE <path> <true|false>`
- `ADD_COMPONENT <path> <componentType>`
- `REMOVE_COMPONENT <path> <componentType>`
- `SET_COMPONENT_FIELD <path> <componentType> <fieldOrProperty> <value>`
- `DELETE_GAMEOBJECT <path>`
- `SELECT_GAMEOBJECT <path>`
- `OPEN_SCENE <path>`
- `SAVE_SCENE`
- `RUN_MENU_ITEM <menuPath>`
- `REFRESH_ASSETS`
- `READ_FILE <path> [maxLines]`
- `LIST_DIR <path>`
- `FIND_IN_FILES <pattern> <rootPath>`
- `GET_GAMEOBJECT_INFO <path>`
- `LIST_SCENE_OBJECTS`
- `GET_SELECTION_INFO`

Tip:
- Quote arguments that contain spaces, e.g. `CREATE_GAMEOBJECT "Enemy Boss"`.

## Agent mode (Cursor-like)

- Enable `Agent mode (multi-step autonomous)` in the window.
- Enter a goal and click `Run Agent`.
- Agent loops for up to `Agent max iterations` and reuses execution feedback each step.
- You can stop a run with `Stop`.
- Optional `Agent dry run` parses actions without applying changes.

Each agent response should end with:

- `AGENT_STATUS: CONTINUE`
- `AGENT_STATUS: DONE`
- `AGENT_STATUS: BLOCKED`

If omitted, the plugin assumes `CONTINUE`.

## Safety notes

- File writes are constrained to project root.
- Scene edits use Undo where possible.
- Keep `Auto apply actions` disabled unless you trust responses.
- Review pending actions before applying.

## Limitations (v0.2)

- No streaming UI yet (response appears when request ends).
- Component field editing supports common primitive/vector/enum values, not arbitrary nested serialization.
- Command parser is text-based and strict.
- Agent loop is single-threaded and does not yet use explicit tool-call JSON/function-calling.
