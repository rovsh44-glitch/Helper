# Conversation Persistence

- Status: accepted
- Date: 2026-03-06
- Canonical ADR: `doc/adr/ADR_CONVERSATION_PERSISTENCE_JOURNAL.md`

## Decision

- Conversation persistence is journal-first and snapshot-backed.
- Interactive turns write into a bounded write-behind queue instead of flushing synchronously.
- Resume uses persisted execution checkpoints.

## Implementation

- `src/Helper.Api/Conversation/FileConversationPersistence.cs`
- `src/Helper.Api/Backend/Persistence/ConversationWriteBehindQueue.cs`
- `src/Helper.Api/Backend/Persistence/ConversationPersistenceWorker.cs`
- `src/Helper.Api/Backend/Application/TurnCheckpointing.cs`
