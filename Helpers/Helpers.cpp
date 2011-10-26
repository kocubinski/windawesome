
#include "stdafx.h"
#include "Helpers.h"

static IShellDispatch2 *psd = NULL;

void RunApplicationNonElevated(const WCHAR* path, const WCHAR* arguments)
{
	if (psd == NULL)
	{
		IShellWindows *psw;
		if (!SUCCEEDED(CoCreateInstance(CLSID_ShellWindows, NULL, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&psw))))
		{
			return ;
		}
		HWND hwnd;
		IDispatch* pdisp;
		VARIANT vEmpty = {}; // VT_EMPTY
		if (S_OK != psw->FindWindowSW(&vEmpty, &vEmpty, SWC_DESKTOP, (long*) &hwnd, SWFO_NEEDDISPATCH, &pdisp))
		{
			return ;
		}
		IShellBrowser *psb;

		if (!SUCCEEDED(IUnknown_QueryService(pdisp, SID_STopLevelBrowser, IID_PPV_ARGS(&psb))))
		{
			return ;
		}
		IShellView *psv;
		psb->QueryActiveShellView(&psv);

		IDispatch *pdispBackground;
		if (!SUCCEEDED(psv->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&pdispBackground))))
		{
			return ;
		}
		IShellFolderViewDual *psfvd;
		if (!SUCCEEDED(pdispBackground->QueryInterface(IID_PPV_ARGS(&psfvd))))
		{
			return ;
		}
		if (!SUCCEEDED(psfvd->get_Application(&pdisp)))
		{
			return ;
		}
		if (!SUCCEEDED(pdisp->QueryInterface(IID_PPV_ARGS(&psd))))
		{
			return ;
		}
	}

	VARIANT args;
	VariantInit(&args);
	args.vt = VT_BSTR;
	args.bstrVal = SysAllocString(arguments);

	VARIANT dir;
	VariantInit(&dir);

	VARIANT operation;
	VariantInit(&operation);
	operation.vt = VT_BSTR;
	operation.bstrVal = SysAllocString(L"open");

	VARIANT show;
	VariantInit(&show);
	show.vt = VT_INT;
	show.intVal = SW_SHOWDEFAULT;

	BSTR p = SysAllocString(path);
	psd->ShellExecuteW(p, args, dir, operation, show);

	SysFreeString(p);
	SysFreeString(operation.bstrVal);
	SysFreeString(args.bstrVal);
}
