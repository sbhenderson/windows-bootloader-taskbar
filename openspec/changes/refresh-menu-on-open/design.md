# Design

## Context

`TrayApplicationContext.RefreshStateAsync` already serializes refreshes through `_refreshGate` and replays a refresh requested while busy. `RebuildMenu` disposes and recreates every item.

## Goals / Non-Goals

**Goals:** fresh state on open, no visible flicker when nothing changed, bounded service load.

**Non-Goals:** blocking the menu until state arrives; polling while the menu is closed.

## Decisions

- **Hook `ContextMenuStrip.Opening` and queue a refresh without cancelling the open.** The menu shows immediately with the last known state; the update lands about one pipe round trip later. Alternative: cancel `Opening`, await the state, then reopen. Rejected: it adds visible latency on every open and re-showing at the cursor is fiddly.
- **Value-equality check before rebuild.** `BootState` is a record, but `ImmutableArray<BootEntry>` compares by reference, so record equality is always false after a refresh. Add `BootStateComparer.AreEquivalent(a, b)` that compares id, timeout, and entries in order with `SequenceEqual`. `RefreshStateAsync` skips `RebuildMenu` when the connection status is unchanged and the states are equivalent. This also removes needless rebuilds from the existing post-mutation refresh.
- **2-second throttle on open only.** The rule lives in a plain `MenuOpenRefreshPolicy` class with an injectable `TimeProvider`, so it is unit-testable without WinForms. Only successful refreshes start the window, so an outage never suppresses a retry on open. Explicit Refresh clicks and post-mutation refreshes ignore the throttle.

## Risks / Trade-offs

- If the state does change while the menu is open, items are rebuilt under the cursor. An open submenu may close. This is rare and preferable to showing stale checkmarks.
