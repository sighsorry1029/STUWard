# STUWard Ward Permission Matrix

This document summarizes how permissions work for `piece_stuward` based on the current code.

Primary reference files:

- `owner / control / access`: [WardAccess.cs](./WardAccess.cs)
- `admin debug`: [WardAdminDebugAccess.cs](./WardAdminDebugAccess.cs)
- `interact / toggle`: [WardUiPatches.cs](./WardUiPatches.cs)
- `settings RPC`: [WardSettings.cs](./WardSettings.cs)
- `build / remove / repair / damage / pickup`: [WardPatches.cs](./WardPatches.cs)

## Scope

Unless noted otherwise, the matrix below describes what a player can do:

- to an existing managed ward, or
- inside the coverage of an enabled managed ward.

## Role Definitions

| Role | Meaning |
|---|---|
| `Owner` | The direct creator of the ward. This is checked by `creator playerId`, not by account id. |
| `Registered` | A player listed in the ward's permitted player list. |
| `Guild Member` | A player whose guild id matches the ward guild id. |
| `Admin+Debug` | A server admin or host with effective debug-control approval. This is the true global override role for managed wards. |
| `Outsider` | Not owner, not permitted, not same guild, and not admin+debug. |

Omitted from the main matrix:

- `Admin`: omitted because admin alone does not get meaningful managed-ward authority and behaves close to `Outsider` for most actions.
- `Debug Only`: omitted because it is mostly a local preview/input state and not a reliable server-authoritative role.

## Legend

| Mark | Meaning |
|---|---|
| `O` | Allowed |
| `X` | Blocked |
| `△` | Conditional |
| `*` | Special case; see notes |

## Unified Permission Matrix

| Action | Owner | Registered | Guild Member | Admin+Debug | Outsider |
|---|---|---|---|---|---|
| Pass managed ward access checks | O | O | O | O | X |
| Interact with an enabled ward without being blocked | O | O | O | O | X |
| Open ward settings UI | O | X | X | O | X |
| Change ward settings | O | X | X | O | X |
| Toggle ward enabled state | O | X | X | O | X |
| Remove another permitted player | O | X | X | O | X |
| Add or remove self from permitted list by interacting with the ward | X | △ | △ | X | △ |
| Place normal build pieces inside ward coverage | O | O | O | O | X |
| Remove normal build pieces inside ward coverage | O | O | O | O | X |
| Repair normal build pieces inside ward coverage | O | O | O | O | X |
| Perform terrain modification inside ward coverage | O | O | O | O | X |
| Damage the managed ward itself | O | X | X | O | X |
| Remove or destroy the managed ward itself | O | X | X | O | X |
| Damage ordinary protected structures inside an enabled ward | * | * | * | * | * |
| Manual item pickup inside a foreign enabled ward | O | O | O | O | △ |
| Auto pickup inside a foreign enabled ward | O | O | O | O | △ |
| Place a new managed ward at an accessible location | O | O | O | O | X |
| Place a managed ward adjacent to another ward in the same trusted group | O | X | △ | O | X |
| Be checked against the per-account ward limit | O | O | O | O | X |
| Be blocked by foreign managed ward overlap rules | O | O | O | O | X |
| See all managed ward minimap pins | X | X | X | O | X |

## Notes For `△` And `*`

### Self-registration by interaction

`Add or remove self from permitted list by interacting with the ward` is `△` because:

- the ward must be `disabled`
- if the player has control rights, interaction goes through the enabled-toggle path instead

This means:

- `Registered`: can remove self while the ward is disabled
- `Guild Member`: can use the same self-toggle path while the ward is disabled
- `Outsider`: can self-register while the ward is disabled

### Item pickup

Manual and auto pickup are `△` for `Outsider` because pickup is also gated by:

- `Pickup Block Mode`
- pickup whitelist
- pickup blacklist

So outsider pickup is not simply always blocked or always allowed.

### Adjacent placement

`Place a managed ward adjacent to another ward in the same trusted group` is `X` for `Registered` and `△` for `Guild Member` because the overlap exemption depends on trusted-group logic, not just basic access.

In practice, the exemption is based on:

- shared owner identity, or
- shared guild identity

The permitted list by itself is not the same as trusted-group overlap exemption.
`Registered` access alone does not allow adjacent managed-ward placement through overlap exemption.

### Protected structure damage

`Damage ordinary protected structures inside an enabled ward` is `*` because this path is not governed only by the simple access matrix.

There is an additional building-damage protection path for:

- friendly ward protection
- unattended monster AI building damage
- source-specific validation

Treat this as a separate damage system, not as a direct mirror of ordinary access/build permissions.

## Important Clarifications

### Owner identity

- Ownership is based on the ward creator player id.
- Account id is used for count, grouping, reporting, and metadata, but not as the direct owner-control test.

### Registered vs Guild Member

- Both pass normal ward access checks.
- Neither can control the ward itself.
- Both may use the self-permitted add/remove path while the ward is disabled.

### Admin+Debug

- `Admin+Debug` is the effective global override role for managed wards.
- It can control foreign managed wards, toggle them, change settings, remove the ward itself, and view all ward pins.

## Behavior Sequences

### 1. Owner opens settings UI and changes radius

1. The player hovers the ward and presses the ward settings shortcut.
2. `WardGuiController.TryOpenHoveredWardUi()` checks `WardAccess.CanConfigureWard(...)`.
3. `Owner` passes because direct owner control is allowed.
4. The UI opens and edits are sent through the ward settings RPC.
5. The server resolves the requester id and checks `CanControlWard(...)`.
6. The radius change is applied, clamped, stored in ZDO, and replicated back.

Result:

- `Owner`: succeeds
- `Admin+Debug`: also succeeds
- everyone else: denied on the server

### 2. Outsider presses `E` on an enabled foreign ward

1. The player interacts with the ward.
2. Interaction logic determines that the player does not have control rights.
3. Because the ward is enabled, the self-permitted toggle path is not available.
4. No enabled toggle is granted.

Result:

- `Outsider`: blocked
- `Registered` and `Guild Member`: still have access, but not control

### 3. Outsider presses `E` on a disabled foreign ward

1. The player interacts with the ward.
2. The player does not have control rights.
3. Because the ward is disabled, the self-permitted path is used.
4. The ward adds or removes that player from the permitted list.

Result:

- `Outsider`: can self-register or self-unregister
- `Registered`: can remove self
- `Guild Member`: can also use the same self-toggle path

### 4. Guild member tries to build inside the ward area

1. The player enters the build placement flow.
2. STUWard access checks run against the placement point.
3. Access evaluation sees matching guild id.
4. Placement is allowed if no other placement-specific rule blocks it.

Result:

- `Guild Member`: allowed
- this does not grant control over the ward itself

### 5. Admin+Debug toggles a foreign ward

1. The player has effective admin-debug control.
2. Interaction enters the control path.
3. The vanilla toggle RPC is sent.
4. The server resolves the authoritative requester id.
5. `CanControlManagedWard(...)` passes because `Admin+Debug` is treated as a controller.
6. The ward enabled state is toggled.

Result:

- `Admin+Debug`: succeeds on any managed ward

### 6. Owner or same-guild player places a ward next to another trusted ward

1. The player starts managed ward placement.
2. STUWard checks ward-limit and access rules for the placement point.
3. Overlap logic evaluates nearby managed wards.
4. If the nearby ward is treated as part of the same trusted group, it is not treated as a foreign-overlap blocker.
5. Placement continues if no foreign ward blocks the radius.

Result:

- `Owner`: can place adjacent to trusted wards
- `Registered`: cannot use the trusted-group overlap exemption by itself
- `Guild Member`: may place adjacent when the trusted-group exemption applies
- `Outsider`: cannot use this exemption against foreign wards

### 7. Outsider tries to auto-pick up an item inside a foreign enabled ward

1. Auto-pickup gathers candidate wards near the player.
2. STUWard computes denied managed ward candidates once for that tick.
3. For each item, pickup policy is checked.
4. If the item is policy-blocked and is inside a denied ward, auto-pickup is skipped.

Result:

- `Outsider`: blocked only for policy-blocked items
- `Registered`, `Guild Member`, `Owner`, and `Admin+Debug`: not blocked by the ward access layer

### 8. Registered player tries to remove the managed ward itself

1. The player targets the ward piece for removal.
2. The removal path detects that the target is the managed ward itself.
3. Removal requires ward control, not just ordinary access.
4. Registered status is not enough.

Result:

- `Registered`: denied
- `Guild Member`: denied
- `Owner`: allowed
- `Admin+Debug`: allowed
