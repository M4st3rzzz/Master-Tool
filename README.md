# Master Tools - Features Guide

***To install, simply extract the "master.dll" file to "SPT\BepInEx\plugins"***

⌨️ Controls

•
INSERT: Open/Close the Mod Menu.

• NumpadKeys





🛡️ General Cheats

•
GodMode: Makes your character immune to all forms of damage. You will not take damage from bullets, falls, or explosions.

•
Infinite Stamina: Keeps your Leg Stamina, Arm Stamina (Aiming), and Oxygen levels at 100% at all times.

•
No Weight Penalties: Removes movement speed and stamina drain penalties caused by carrying heavy gear or loot.

•
Unlock All Doors: A powerful utility that instantly unlocks every locked door and container on the map during a raid. No keys required.






👥 Player ESP

Visualizes all players and bots through walls with the following features:

•
Faction Identification: Different colors for BEAR, USEC, Bosses and Savages (Scavs/Raiders).

•
Distance Tracking: Shows the exact distance in meters to each target.

•
Customizable Colors: Full RGB sliders to change faction colors in real-time.

•
Performance Control: Adjustable Update Rate (FPS) slider to balance between smooth visuals and CPU performance.

•
Distance Filter: Set a maximum range to avoid screen clutter.





📦 Item & Container ESP

Advanced loot tracking system optimized for high performance:

•
Loose Item ESP: Shows items lying on the ground.

•
Container Item ESP: Shows items hidden inside crates, jackets, safes, and dead bodies.

•
Multi-Filter System: Search for specific items by entering names or IDs. Supports comma-separated lists (e.g., LedX, GPU, Salewa).

•
Optimized Performance: Uses a smart caching system for containers and squared-distance calculations to ensure zero FPS drops even on loot-heavy maps.

•
Update Rate (FPS): Independent refresh rate control for loot visuals.





⚙️ Technical Details

• Optimization: 

  - Features a container caching system that refreshes every 10 seconds to maintain high frame rates. 

  - Culling provides a small performance boost by disabling meshes at distance. ⚠️ However, this may cause bots to appear instantly once they enter the configured range. 

• Config file on -> "SPT\BepInEx\config\com.master.tools.cfg"
