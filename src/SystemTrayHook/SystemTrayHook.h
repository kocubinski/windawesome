
#pragma once

#define SYSTEMTRAYHOOK_API __declspec(dllexport)

SYSTEMTRAYHOOK_API BOOL __cdecl RegisterSystemTrayHook(HWND hWnd);
SYSTEMTRAYHOOK_API BOOL __cdecl UnregisterSystemTrayHook();

#undef SYSTEMTRAYHOOK_API
