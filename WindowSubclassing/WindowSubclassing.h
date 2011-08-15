
#pragma once

#define WINDOWSUBCLASSING_API __declspec(dllexport)

WINDOWSUBCLASSING_API BOOL __cdecl SubclassWindow(HWND hWnd, HWND window);
WINDOWSUBCLASSING_API BOOL __cdecl UnsubclassWindow(HWND window);

#undef WINDOWSUBCLASSING_API
