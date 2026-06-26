# NORS - Nuclear Option Radio System

A realistic radio communication system for Nuclear Option.

NORS (Nuclear Option Radio System) brings immersive, frequency-based voice communications to Nuclear Option, inspired by systems such as DCS-SRS while being built specifically for the Nuclear Option multiplayer environment.

As of **v0.3.0**, NORS introduces **Peer-to-Peer (P2P) Voice Networking** as the default communication method, allowing players connected to the same Nuclear Option multiplayer server to communicate directly without requiring a dedicated relay server. For communities that prefer centralized hosting, dedicated relay servers remain fully supported with advanced administration and moderation features.

The project combines realistic radio operations, secure faction communications, terrain-aware signal propagation, radio horizon calculations, and high-quality voice networking to make communication a natural part of the battlefield.

---

# Overview

Nuclear Option provides an excellent combat flight experience but lacks integrated voice communications. NORS fills that gap by providing a complete radio ecosystem designed for both casual multiplayer sessions and organized squadrons.

With the introduction of Peer-to-Peer networking, most players no longer need to host or connect to a relay server. NORS automatically establishes direct voice connections between players on the same multiplayer server, reducing latency and simplifying setup.

Dedicated relay servers remain available for communities, public servers, and events that require centralized routing and moderation.

---

# Key Features

## Voice Communications

* Peer-to-Peer voice networking (Default)
* Optional dedicated relay server
* Real-time low-latency voice transmission
* Opus audio encoding
* Push-to-talk operation
* Multiple simultaneous radios
* Automatic peer discovery and connection management

## Radio Simulation

* Frequency-based communications
* AM and FM modulation
* Secure and Clear transmission modes
* Independent radio monitoring
* Configurable radio volumes
* Multiple transmit and receive radios

## Radio Propagation

* Distance-based signal attenuation
* Radio horizon simulation
* Altitude-dependent communication range
* Terrain line-of-sight occlusion
* Dynamic signal degradation
* Radio static and interference effects
* Signal quality simulation

## Multiplayer Integration

* Automatic Peer-to-Peer networking
* Optional relay networking
* Faction-aware secure communications
* Open guard frequencies
* Remote administration
* Kick and ban management
* Persistent bans
* Faction-based vote kicking
* Multi-room relay support

---

# How It Works

NORS uses a hybrid networking architecture.

By default, players communicate directly using Peer-to-Peer voice connections whenever they join the same Nuclear Option multiplayer server. This eliminates the need for additional voice infrastructure while significantly reducing latency.

For communities requiring centralized management, NORS also supports dedicated relay servers that provide moderation tools, persistent bans, remote administration, and multi-room support.

Regardless of the networking mode, every client independently performs the radio simulation using live game data.

NORS simulates:

* Aircraft position
* Radio range
* Radio horizon
* Terrain obstruction
* Signal attenuation
* Signal quality
* Secure communications
* Radio effects

without requiring modifications to Nuclear Option's networking infrastructure.

---

# Installation

## Client

1. Install BepInEx for Nuclear Option.
2. Download the latest NORS release.
3. Extract the release files into:

```text
BepInEx/plugins/NORS/
```

4. Launch Nuclear Option.
5. Join any multiplayer server.

> **Note:** Peer-to-Peer networking is enabled by default. A relay server is only required if you choose to use relay mode.

---

## Relay Server (Optional)

Relay hosting is optional but remains available for communities that prefer centralized voice routing.

Supported platforms:

* Windows GUI
* Windows Console
* Linux x64
* macOS Intel
* macOS Apple Silicon

Hosting a relay server:

1. Download the latest relay release.
2. Launch either:

   * `NORS.ServerGUI.exe`
   * `NORS.Server.exe`
3. Configure the listening port.
4. Open UDP port **5555** on your firewall/router.
5. Share the server address with players.

---

# Default Controls

| Key    | Action                      |
| ------ | --------------------------- |
| **F7** | Open Radio Panel            |
| **T**  | Push-to-Talk                |
| **Y**  | Cycle Active Transmit Radio |
| **,**  | Tune Frequency Down         |
| **.**  | Tune Frequency Up           |

---

# System Requirements

## Client

* Nuclear Option
* BepInEx
* Windows

## Relay Server (Optional)

* Windows, Linux, or macOS
* .NET Runtime
* UDP Port **5555** (default)

---

# Current Status

**Version:** **v0.3.0**

## Implemented

* Peer-to-Peer voice networking
* Optional dedicated relay networking
* Cross-platform relay support
* Voice networking
* Frequency routing
* Radio management
* Multiple radios
* Secure communications
* Radio propagation simulation
* Terrain-aware signal attenuation
* Radio horizon calculations
* Radio effects
* Remote administration
* Persistent bans
* Multi-room relay support
* Faction vote kicking
* Multiplayer support

## Ongoing Development

* Audio balancing
* Signal tuning
* Networking optimizations
* Additional radio functionality
* Expanded administration tools
* Quality-of-life improvements
* Additional realism features

---

# Contributing

Feedback, bug reports, feature requests, and pull requests are always welcome.

Community testing plays a major role in improving NORS and shaping future releases.

---

# Acknowledgements

NORS is heavily inspired by the radio communication systems found in modern combat flight simulators while being designed specifically for Nuclear Option and its unique multiplayer environment.

---

# NORS - Nuclear Option Radio System

*Realistic communications for modern aerial warfare.*
