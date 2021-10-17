<p align="center"><img src="https://www.dropbox.com/s/9c4ovllw064gtnz/JireLogoCondensed.png?raw=1" /></p>

Jire will be an open-source RTS that features substantial modifications to the real-time strategy game engine OpenRA ([http://www.openra.net](http://www.openra.net)). The primary goal of Jire is to further enable modern RTS development within OpenRA (ORA), by incorporating modern features such as non-grid based movement and cutting edge pathfinding. This will in turn enable the creation (or re-creation) of RTS games such as Age Of Empires II, StarCraft II, C&C 3 and more with less rework required. The secondary goal of Jire is to be a retro-inspired competitive multiplayer RTS, that feels modern and takes inspiration from the all-time greats such as Age of Empires II and StarCraft/StarCraft II.

Below are a list of core features that will be developed for this purpose.

- **(DONE)** Implement Non-grid based movement using the existing Aircraft movement as a basis
- **(DONE)** Implement Non-grid based collision detection with other buildings and units
- **(DONE)** Implement Non-grid based collision detection with terrain obstacles such as cliffs and rivers
- **(HALF DONE)** Modify or re-implement the pathfinding module for non-grid based movement. An example of such a module can be found with [Age Of Empires II](https://www.gamasutra.com/view/feature/131720/coordinated_unit_movement.php) or StarCraft II.
  - **(DONE)** Implement Theta Star Pathfinder
  - **(IN PROGRESS)** Implement Traffic flow to allow units to hug walls and other units during movement

In addition, the below are nice-to-have features that will be developed once the core features are complete.

- Diamond based grid for building placement as it exists in Age Of Empires II.
- A game balance framework and/or theory that aims to ensure a baseline level of balance among factions can easily be obtained, that avoids as much painstaking trial and error as possible.
- Client/server architecture, with a competitive matchmaking system that uses Elo to group players into leagues (similar to StarCraft II).
- A custom map/game module that enables people to create their own spin-off mini-games within the engine, similar to that seen in StarCraft & WarCraft custom maps.
