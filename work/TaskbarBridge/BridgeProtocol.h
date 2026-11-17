#pragma once
#include <windows.h>
#include <cstdint>
inline constexpr GUID BridgeClass = {0x717163a7,0x1a1c,0x4717,{0xa3,0x13,0x71,0x0e,0x7e,0x62,0x71,0x48}};
inline constexpr uint32_t BridgeMagic=0x4C425231;
// Single writer per half. Even sequences represent committed snapshots.
struct alignas(8) BridgeMemory {
    uint32_t magic,version;
    volatile LONG requestSequence;
    uint32_t mode; // 0=inspect, 1=reserve, 2=release and disconnect
    double widthDip;
    uint64_t heartbeat;
    uint64_t taskbar;
    uint32_t ownerPid;
    volatile LONG responseSequence;
    uint32_t status; // 1=attached, 2=compatible, 3=reserved, 4=released, 5=unsupported, 6=failed, 7=layer-only
    int32_t error;
    uint64_t responseAt;
    RECT slot;
    double actualDip;
    uint64_t targetHandle;
    volatile LONG callbackCount,candidateCount;
    int32_t discoverError;
    uint32_t containerKind;
    uint64_t musicHwnd,weatherHwnd;
    uint32_t capsulePid;
    int32_t layerError;
    uint32_t taskbarBand,layerRepairs;
};
static_assert(offsetof(BridgeMemory,slot)==64);
static_assert(sizeof(BridgeMemory)==144);
