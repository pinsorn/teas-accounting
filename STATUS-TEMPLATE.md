# STATUS.md — orchestrator live board

<!-- Copy to STATUS.md when real work starts (delete this comment block in
     the copy). Update at EVERY phase boundary — after each dispatch batch,
     verification, and commit. This is the "what is happening right now"
     view; PROGRESS-<task>.md remains the detailed durable checkpoint
     written at quota cliffs / pauses. The session-orient hook injects the
     first 12 non-blank lines at every session start — keep the head
     ("## Now") current and compact. -->

## Now
- Goal: <what this project is trying to ship>
- Phase: <plan / dispatch / verify / commit — which task>
- In-flight: <worker + spec path, or none>
- Warm workers: <role → area it holds context on, or none — a warm worker
  resumed via SendMessage beats a cold re-spawn for same-area follow-ups;
  after compaction/crash this line is the only reminder they exist>
- Next: <the next dispatch or gate>

## Recently done
- <date> <unit of work> — commit <hash>

## Blocked / waiting
- <anything waiting on user, quota, or external — or none>
