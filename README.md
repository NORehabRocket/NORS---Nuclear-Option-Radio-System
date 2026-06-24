# NORS - Nuclear Option Radio System

A realistic radio communication system for Nuclear Option.

NORS (Nuclear Option Radio System) brings immersive, frequency-based voice communications to Nuclear Option, inspired by systems such as DCS-SRS while being built specifically for the Nuclear Option multiplayer environment.

The project introduces realistic radio operations, secure faction communications, terrain-aware signal propagation, radio horizon calculations, and dedicated voice networking, allowing communication to become an integral part of gameplay rather than an external tool.

---

## Overview

Nuclear Option provides an excellent combat flight experience but lacks integrated voice communications. NORS fills that gap by providing a complete radio ecosystem that supports both casual multiplayer coordination and organized squadron operations.

Communications are routed through a lightweight relay server while each client independently simulates radio propagation, signal quality, and environmental effects based on real in-game conditions.

---

## Key Features

### Voice Communications

* Real-time low-latency voice transmission
* Opus audio encoding
* Push-to-talk operation
* Multiple simultaneous radios
* Dedicated relay server architecture

### Radio Simulation

* Frequency-based communications
* AM and FM modulation modes
* Secure and clear transmission modes
* Independent radio monitoring
* Configurable radio volumes

### Propagation System

* Distance-based signal attenuation
* Radio horizon simulation
* Altitude-dependent communication range
* Terrain line-of-sight occlusion
* Dynamic signal degradation
* Static and interference effects

### Multiplayer Integration

* Faction-aware secure communications
* Open guard frequencies
* Dedicated server administration tools
* Remote administration support
* Kick and ban management
* Faction-based vote kicking

---

## How It Works

NORS uses a standalone voice networking architecture similar to modern flight simulator radio systems.

A relay server routes voice traffic between connected clients while the game plugin performs all radio simulation locally using real-time game data.

This allows NORS to simulate:

* Aircraft position
* Radio range
* Terrain obstruction
* Signal quality
* Faction security
* Radio effects

without requiring modifications to the game's networking infrastructure.

---

## Installation

### Client

1. Install BepInEx for Nuclear Option.
2. Download the latest NORS release.
3. Extract the release files into:

```text
BepInEx/plugins/NORS/
```

4. Launch Nuclear Option.
5. Configure the relay server address.
6. Join a multiplayer session.

### Relay Server

1. Download the latest server release.
2. Launch either:

   * `NORS.ServerGUI.exe`
   * `NORS.Server.exe`
3. Configure the desired listening port.
4. Open UDP port 5555 on your firewall/router.
5. Share the server address with players.

---

## Default Controls

| Key | Action               |
| --- | -------------------- |
| F7  | Open Radio Panel     |
| T   | Push-To-Talk         |
| Y   | Cycle Transmit Radio |
| ,   | Tune Frequency Down  |
| .   | Tune Frequency Up    |

---

## System Requirements

### Client

* Nuclear Option
* BepInEx
* Windows
* Network connection to a NORS relay server

### Server

* Windows
* .NET Runtime
* Open UDP port (default: 5555)

---

## Current Status

**Version:** v0.1.0

### Implemented

* Voice networking
* Relay server
* Frequency routing
* Radio management
* Secure communications
* Radio propagation simulation
* Terrain-aware signal attenuation
* Administration tools
* Multiplayer support

### Ongoing Development

* Audio balancing
* Signal effect tuning
* Latency optimization
* Additional radio functionality
* Expanded administration features

---

## Contributing

Feedback, bug reports, and feature requests are welcome through GitHub Issues.

Community testing is invaluable in helping improve NORS and expand future functionality.


---

## Acknowledgements

NORS is heavily inspired by the radio communication systems used in modern combat flight simulators and aims to bring a similar level of immersion and tactical coordination to Nuclear Option.

---

**NORS — Nuclear Option Radio System**

*Realistic communications for modern aerial warfare.*
