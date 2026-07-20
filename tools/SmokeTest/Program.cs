using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NORS.Common;
using NORS.Server.Core;

// In-process end-to-end check of the multi-room relay:
// routing, ROOM ISOLATION, vote-kick (room+faction), room-host moderation, room-scoped bans,
// cross-room admin denial, and master (password) admin.

int port = 5603;
int pass = 0, fail = 0;
string banFile = Path.Combine(Path.GetTempPath(), "nors-smoke-bans.txt");
if (File.Exists(banFile)) File.Delete(banFile);

var server = new RelayServer(port, "SmokeRelay", banFile, adminPassword: "secret", voteKickEnabled: true);
server.Start();
Thread.Sleep(100);

UdpClient Make() { var c = new UdpClient(); c.Connect("127.0.0.1", port); c.Client.ReceiveTimeout = 700; return c; }
byte[] Recv(UdpClient c) { try { IPEndPoint ep = null; return c.Receive(ref ep); } catch (SocketException) { return null; } }
int Type(byte[] d) => (d != null && d.Length >= 2) ? d[1] : -1;
byte[] RecvOf(UdpClient c, PacketType t) { while (true) { var d = Recv(c); if (d == null) return null; if (Type(d) == (int)t) return d; } }
bool AdminOk(byte[] d) { var r = new PacketReader(d, 0, d.Length); r.Byte(); r.Byte(); return r.Bool(); }
void Check(string label, bool ok, string extra = "") { Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label} {extra}"); if (ok) pass++; else fail++; }

var w = new PacketWriter();
var A = Make(); var B = Make(); var C = Make(); var D = Make(); var E = Make();
const uint AId = 1001, BId = 2002, CId = 3003, DId = 4004, EId = 5005;
const ulong AS = 11111, BS = 22222, CS = 33333, DS = 44444, ES = 55555;
const ulong R1 = 100, R2 = 200;     // two separate game "servers"
const int F7 = 7, F9 = 9;

void Hello(UdpClient c, uint id, ulong steam, ulong room, bool host, int fac, string name)
{ Packets.WriteHello(w, id, steam, room, host, fac, name); c.Send(w.Buffer, w.Length); }
void State(UdpClient c, uint id, ulong steam, ulong room, bool host, int fac, int[] rx, string name)
{ Packets.WriteState(w, id, steam, room, host, fac, 0, 1000, 0, rx, rx.Length, name); c.Send(w.Buffer, w.Length); }
void Voice(UdpClient c, uint id, int freq) { var a = new byte[8]; Packets.WriteVoice(w, id, 1, freq, Modulation.AM, F7, 0, 0, 1000, 0, a, 0, 8, "x", 0); c.Send(w.Buffer, w.Length); }

// ---- handshake: A(host),B,C,D in room R1; E(host) in room R2 ----
Hello(A, AId, AS, R1, true, F7, "Viper"); Check("A joins (host R1)", RecvOf(A, PacketType.HelloAck) != null);
Hello(B, BId, BS, R1, false, F7, "Hornet"); Check("B joins (R1)", RecvOf(B, PacketType.HelloAck) != null);
Hello(E, EId, ES, R2, true, F7, "Other"); Check("E joins (host R2)", RecvOf(E, PacketType.HelloAck) != null);

State(A, AId, AS, R1, true, F7, new[] { 251000 }, "Viper");
State(B, BId, BS, R1, false, F7, new[] { 251000 }, "Hornet");
State(E, EId, ES, R2, true, F7, new[] { 251000 }, "Other");
Thread.Sleep(120);

// ---- routing within a room + ISOLATION across rooms ----
Voice(A, AId, 251000);
Check("same-room teammate hears voice", RecvOf(B, PacketType.Voice) != null);
Check("other server does NOT hear voice", RecvOf(E, PacketType.Voice) == null);

// ---- vote-kick: A+B (R1,F7) kick C (R1,F7) ----
Hello(C, CId, CS, R1, false, F7, "Bandit"); RecvOf(C, PacketType.HelloAck);
State(C, CId, CS, R1, false, F7, new[] { 251000 }, "Bandit");
Thread.Sleep(120);
Packets.WriteVoteKick(w, AId, CId); A.Send(w.Buffer, w.Length); RecvOf(A, PacketType.Notice);
Packets.WriteVoteKick(w, BId, CId); B.Send(w.Buffer, w.Length);
Check("same room+faction majority kicks C", RecvOf(C, PacketType.Kicked) != null);

// ---- cross-faction vote refused (D is R1 but faction 9) ----
Hello(D, DId, DS, R1, false, F9, "Spy"); RecvOf(D, PacketType.HelloAck);
State(D, DId, DS, R1, false, F9, new[] { 251000 }, "Spy");
Thread.Sleep(120);
Packets.WriteVoteKick(w, DId, AId); D.Send(w.Buffer, w.Length);
Check("cross-faction vote refused", RecvOf(D, PacketType.Notice) != null);
Thread.Sleep(100);
// Connected now: A, B, E, D (C was vote-kicked earlier).
Check("cross-faction target not kicked", server.ClientCount == 4, $"(={server.ClientCount})");

// ---- room-host moderation: A (host R1) kicks D (R1) with NO password ----
Packets.WriteAdminCommand(w, AId, AdminOp.Kick, DId, "host kick"); A.Send(w.Buffer, w.Length);
Check("host kicks own-room player", RecvOf(D, PacketType.Kicked) != null);
var hostKickRes = RecvOf(A, PacketType.AdminResult);
Check("host kick authorized", hostKickRes != null && AdminOk(hostKickRes));

// ---- host CANNOT moderate another server: A (R1) tries to kick E (R2) ----
Packets.WriteAdminCommand(w, AId, AdminOp.Kick, EId, "nope"); A.Send(w.Buffer, w.Length);
var crossRoom = RecvOf(A, PacketType.AdminResult);
Check("host cannot kick other server's player", crossRoom != null && !AdminOk(crossRoom));
Thread.Sleep(100);
// Connected now: A, B, E (D was host-kicked just above); E unaffected by the denied cross-room kick.
Check("other server's player still connected", server.ClientCount == 3, $"(={server.ClientCount})");

// ---- room-scoped ban: A bans B from R1; B blocked in R1 but allowed in R2 ----
Packets.WriteAdminCommand(w, AId, AdminOp.Ban, BId, "cheating"); A.Send(w.Buffer, w.Length);
Check("host bans own-room player", RecvOf(B, PacketType.Kicked) != null);
Hello(B, BId, BS, R1, false, F7, "Hornet");
Check("banned player blocked rejoining that room", RecvOf(B, PacketType.Reject) != null);
Hello(B, BId, BS, R2, false, F7, "Hornet");
Check("same player allowed on a different server", RecvOf(B, PacketType.HelloAck) != null);

// ---- master (password) admin can moderate across rooms ----
Packets.WriteAdminAuth(w, EId, "secret"); E.Send(w.Buffer, w.Length);
Check("master password authenticates", AdminOk(RecvOf(E, PacketType.AdminResult)));
Packets.WriteAdminCommand(w, EId, AdminOp.Kick, AId, "master kick"); E.Send(w.Buffer, w.Length);  // E(R2) kicks A(R1)
Check("master admin kicks across rooms", RecvOf(A, PacketType.Kicked) != null);

A.Close(); B.Close(); C.Close(); D.Close(); E.Close();
server.Stop();
try { File.Delete(banFile); } catch { }

Console.WriteLine($"\n{pass} passed, {fail} failed.");
Environment.ExitCode = fail == 0 ? 0 : 1;
