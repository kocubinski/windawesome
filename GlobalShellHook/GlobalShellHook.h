// The following ifdef block is the standard way of creating macros which make exporting
// from a DLL simpler. All files within this DLL are compiled with the GLOBALSHELLHOOK_EXPORTS
// symbol defined on the command line. This symbol should not be defined on any project
// that uses this DLL. This way any other project whose source files include this file see
// GLOBALSHELLHOOK_API functions as being imported from a DLL, whereas this DLL sees symbols
// defined with this macro as being exported.
#define GLOBALSHELLHOOK_API __declspec(dllexport)

extern "C" {
GLOBALSHELLHOOK_API void __cdecl RegisterGlobalShellHook(HWND handle);
GLOBALSHELLHOOK_API void __cdecl UnregisterGlobalShellHook();
}
