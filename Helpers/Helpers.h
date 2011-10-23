
#pragma once

#define HELPERS_API __declspec(dllexport)

extern "C" {
HELPERS_API void __cdecl RunApplicationNonElevated(const WCHAR*, const WCHAR*);
}

#undef HELPERS_API
