#pragma once

#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>

// Keep these identifiers in sync with ExplorerContextMenuRegistration.cs and ExplorerDropTargetServer.cs.
inline constexpr CLSID CLSID_ClipDiffShellExtension =
    {0x6b46a974, 0x40e2, 0x4ad4, {0x9f, 0x68, 0xe5, 0x34, 0x20, 0x2b, 0x11, 0xe8}};
inline constexpr CLSID CLSID_ClipDiffDropTarget =
    {0x4d22fa39, 0x9e5d, 0x42bd, {0xbf, 0x0a, 0x8a, 0xe8, 0x85, 0x70, 0x4e, 0xc7}};
inline constexpr wchar_t ClipDiffReadyEvent[] = L"Local\\ClipDiff.ExplorerPairReady";
inline constexpr wchar_t ClipDiffMenuLabel[] = L"Compare two selected files with ClipDiff";
inline constexpr wchar_t ClipDiffVerb[] = L"ClipDiff.CompareSelected";
inline constexpr char ClipDiffVerbAnsi[] = "ClipDiff.CompareSelected";
