<p align="center"><img src="https://www.dropbox.com/s/9c4ovllw064gtnz/JireLogoCondensed.png?raw=1" /></p>

Jire will be an open-source RTS that features substantial modifications to the real-time strategy game engine OpenRA ([http://www.openra.net](http://www.openra.net)). The primary goal of Jire is to further enable modern RTS development within OpenRA (ORA), by incorporating modern features such as non-grid based movement and cutting edge pathfinding. This will in turn enable the creation (or re-creation) of RTS games such as Age Of Empires II, StarCraft II, C&C 3 and more with less rework required. The secondary goal of Jire is to be a retro-inspired competitive multiplayer RTS, that feels modern and takes inspiration from the all-time greats such as Age of Empires II and StarCraft/StarCraft II.

Below are a list of core features that will be developed for this purpose. 

***Update: These have been completed at a basic level. More work is required to add polish to this, to remove bugs and make it look more aesthetically pleasing***

- **(DONE)** Implement Non-grid based movement using the existing Aircraft movement as a basis
- **(DONE)** Implement Non-grid based collision detection with other buildings and units
- **(DONE)** Implement Non-grid based collision detection with terrain obstacles such as cliffs and rivers
- **(DONE)** Modify or re-implement the pathfinding module for non-grid based movement. An example of such a module can be found with [Age Of Empires II](https://www.gamasutra.com/view/feature/131720/coordinated_unit_movement.php) or StarCraft II.
  - **(DONE)** Implement Theta Star Pathfinder
  - **(DONE)** Implement Local Avoidance algorithm to allow units to hug walls and other units during movement

***Known Issues To Solve:***

- The Theta Star Pathfinder is not optimised, and takes a long time to calculate even moderate to long paths (it will freeze when calculating on very large maps). A similar optimisation to OpenRA's A\* algorithm, or another optimisation needs to be sought. It also appears to expand far more than the original A\* algorithm, however this may be due to subtleties in how they were designed. The algorithm looks correct and truthful to the original C++ implementation of this algorithm, so care must be taken to not alter the correctness when optimising.

- For group movement, a target is currently reached when any unit within a Radius of the unit has reached the target. This is to prevent units fighting for a single target point on the map. However, since a Radius is circular, this means units will reach a corner target before they have passed the wall, causing them to try and 'push' through the wall to the next target. One way to solve this may be to draw an invisible line at each corner, and only consider the target complete when the unit crosses this line.

- For group movement, units will occasionally get stuck on each other, and 'twitch' without finding the target, even when they are very close to it, due to the repulsion effect preventing them from getting close enough. This could be avoided by adding a radius to each target that is at least as big as the unit.

- For group movement involving large sets of units, there is noticeable lag. This may be alleviated through adjusting the game speed, but a better solution should be sought.

- For group movement involving large sets of units, units appear to 'merge' into eachother, violating the restriction on their unit radius. Some additional rules around the repulsion vectors may solve this.

- Movement appears 'jittery' despite following the correct path. Perhaps some Lerping or interpolation could be used here to make it appear more pleasing to the user.

- Unit rotation animations currently look abrupt. The position buffer should be used to interpolate the rotation animation across multiple frames, and only change the animation when the average difference in angle over multiple frames is sufficient, rather than the difference in a single frame as it is now.

***Nice To Have's***

In addition, the below are nice-to-have features that will be developed once the core features are complete.

- Diamond based grid for building placement as it exists in Age Of Empires II.
- A game balance framework and/or theory that aims to ensure a baseline level of balance among factions can easily be obtained, that avoids as much painstaking trial and error as possible.
- Client/server architecture, with a competitive matchmaking system that uses Elo to group players into leagues (similar to StarCraft II).
- A custom map/game module that enables people to create their own spin-off mini-games within the engine, similar to that seen in StarCraft & WarCraft custom maps.
