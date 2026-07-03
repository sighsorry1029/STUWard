# STU Ward

Simple, Tidy, and Unique Ward for Valheim servers. <br>
It adds a clone of vanilla ward but with more server-side features such as diverse protections, permission control, guild integration, ward count limits, and compatibility handling for common utility mods.
<br>
![](https://i.ibb.co/Q7GkMgvB/Screenshot-2026-03-31-031730.png)<br>
Registration is same with vanilla ward. <br>
There is blacklist config to block certain items inside ward area.

![](https://i.ibb.co/HLH084PV/Screenshot-2026-03-31-024031.png)<br>
Ward Settings UI <br>

![](https://i.ibb.co/gZRSPGrr/Screenshot-2026-03-30-234415.png)
Ward area cannot be overlapped unless there is guild or one player built all the wards.<br>

![](https://i.ibb.co/v42DSpST/Video-Project-8.gif) <br>
Good old auto closing door inside ward area. <br>

## What It Does

- Adds a placeable `Ward` with server-controlled protection rules
- Lets ward owners configure radius, marker visibility, warning effects, and door auto-close
- Blocks unauthorized interaction, building, terrain edits, pickup, item use, and damage inside enabled foreign wards
- Prevents foreign ward overlap while allowing trusted shared ward groups
- Tracks per-account ward limits
- Shows ward pins and active ranges on the map when allowed

## How To Use

1. Select `Ward` with the hammer and place it.
2. A new ward starts at `8m` radius.
3. Look at your ward and press `Alt+E` to open `Ward Settings`.
4. Adjust radius, marker display, warning effects, and auto-close delay.
5. To register a player, turn the ward off and have that player interact with it.

While placing a ward, the placement preview shows the maximum configurable radius.

## Protection

Inside an enabled ward, unauthorized players are blocked from:

- Opening or using containers, doors, carts, ships, signs, item stands, beehives, crafting stations, fermenters, sap collectors, traps, portals, and tamed creatures
- Building, repairing, removing pieces, or modifying terrain
- Damaging protected structures and objects
- Picking up items, including auto-pickup when the item policy blocks it
- Using or equipping blocked item prefabs
- Using creature-catching items on protected tamed animals

Building pieces inside an enabled STU Ward receive extra damage protection. Player and tamed-creature damage to protected building pieces is blocked, and hostile creature damage can also be blocked depending on ward attendance settings.

## Permissions

STU Ward separates access from control.

Players with access can use protected areas without being blocked. Players with control can change the ward itself.

Access is granted to:

- The ward owner
- Registered players
- Players matching the ward's stored guild identity
- Server admins using effective debug control

Control is limited to:

- The ward owner
- Server admins using effective debug control

Registered players and guild members can use the protected area, but they cannot change settings, toggle the ward, remove other registered players, or remove the ward itself.

## Registration

Registration works like a vanilla ward:

- The ward must be disabled.
- A player interacts with the ward to add or remove themselves.
- Players with control rights use interaction to toggle the ward instead.

This means a disabled ward is intentionally open for self-registration.

## Ward Overlap

Ward overlap is strict.

- Foreign wards cannot overlap.
- Same-owner wards can overlap.
- Wards with the same stored guild identity can overlap.
- Registered-player access does not bypass overlap rules.

In overlapping coverage, access is additive: if any enabled foreign ward denies the player, the action is denied.

## Ward Settings

Each ward can store its own behavior:

- Radius
- Area marker visibility
- Area marker brightness
- Warning sound
- Warning flash
- Door auto-close delay

Door auto-close uses the shortest active delay from the wards covering the door.

## Item Policy

Servers can define blocked item prefabs and pickup rules.

Blocked item prefabs cannot be used, equipped, or used to attack while the player is inside a foreign enabled ward. Pickup rules can either block everything except a whitelist or allow everything except a blacklist.

## Map Pins

Ward pins can show ward locations and active ranges on the map.

Players normally see wards they are allowed to see. Admin debug control can show all managed wards.

## Important Details

- Ownership is based on the ward creator player id.
- Account identity is used for limits and reporting, not direct ward control.
- A ward's guild identity is stored on the ward when metadata is captured.
- Changing guild later does not automatically rewrite old wards.
- To move a ward to a different shared group, remove it and place a new one.

## Github
https://github.com/sighsorry1029/STUWard