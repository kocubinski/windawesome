
#include "stdafx.h"

extern HINSTANCE hInstance;
extern UINT START_WINDOW_PROC_MESSAGE;
extern UINT STOP_WINDOW_PROC_MESSAGE;

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		hInstance = hModule;
		if (!START_WINDOW_PROC_MESSAGE)
		{
			START_WINDOW_PROC_MESSAGE = RegisterWindowMessage(TEXT("START_WINDOW_PROC"));
			STOP_WINDOW_PROC_MESSAGE = RegisterWindowMessage(TEXT("STOP_WINDOW_PROC"));
		}
		break;
	}
	return TRUE;
}

