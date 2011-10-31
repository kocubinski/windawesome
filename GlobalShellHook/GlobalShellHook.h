
#pragma once

#define GLOBALSHELLHOOK_API __declspec(dllexport)

GLOBALSHELLHOOK_API BOOL __cdecl RegisterGlobalShellHook(HWND handle);
GLOBALSHELLHOOK_API BOOL __cdecl UnregisterGlobalShellHook();

#undef GLOBALSHELLHOOK_API
