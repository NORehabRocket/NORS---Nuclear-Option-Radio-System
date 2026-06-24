NORS - Nuclear Option Radio System

Bring realistic radio communications to Nuclear Option.

NORS (Nuclear Option Radio System) adds immersive DCS-SRS inspired voice communications directly into Nuclear Option, transforming multiplayer coordination with realistic radio behavior, secure communications, frequency management, and dynamic signal propagation.

Instead of relying solely on external voice applications, NORS integrates communication into the battlefield itself. Your altitude, distance, radio settings, and surrounding terrain all affect how well you can communicate with other pilots.

Features
Real-Time Voice Communications
Low-latency Opus voice codec
Push-to-talk operation
Multiple simultaneous radio channels
Dedicated relay server support
Advanced Radio Simulation
AM and FM radio modes
Frequency-based communication
Multiple independent cockpit radios
Secure and clear transmission modes
Realistic Signal Propagation
Distance-based signal degradation
Radio horizon calculations
Altitude affects transmission range
Terrain and mountain signal blocking
Radio static and interference effects
Modulation mismatch simulation
Multiplayer Focused
Faction-secure communications
Open guard frequencies
Dedicated relay server
Remote administration support
Kick and ban management
Faction-based vote kicking
Requirements
Client
Nuclear Option
BepInEx
NORS Plugin Files
Server Host
Windows
UDP Port 5555 Open
NORS Relay Server
Installation
Client Installation
Install BepInEx for Nuclear Option.
Download the latest NORS release.
Extract the contents into:
Nuclear Option/BepInEx/plugins/NORS/
Launch Nuclear Option.
Configure your server address in the NORS configuration file or through Configuration Manager.
Hosting a Relay Server
Download the relay server package.
Launch either:
NORS.ServerGUI.exe
NORS.Server.exe
Open UDP Port 5555 on your firewall/router.
Share your public IP with players.
Default Controls
Key	Function
F7	Open Radio Panel
T	Push-To-Talk
Y	Cycle Active Radio
,	Tune Frequency Down
.	Tune Frequency Up
Radio Features

NORS simulates realistic radio behavior:

Higher altitude increases communication range.
Terrain can block transmissions.
Signal quality decreases with distance.
Secure transmissions are restricted to friendly forces.
Clear transmissions can be monitored by anyone on frequency.
Weak signals produce static and distortion.

Communication becomes part of the mission, not just a background tool.

Current Version

v0.1.0

Included
Voice networking
Relay server
Secure communications
Frequency routing
Radio propagation system
Terrain line-of-sight simulation
Administration tools
Multiplayer support
Download

Download the latest release from the Releases section of this repository.

Feedback & Support

Bug reports, feature requests, and feedback are welcome through GitHub Issues.

Community testing and feedback help improve NORS and shape future updates.

NORS — Nuclear Option Radio System

"Communication is a weapon. Use it wisely."
