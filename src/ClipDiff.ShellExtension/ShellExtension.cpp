#include "ShellExtension.h"
#include <wrl/client.h>
#include <strsafe.h>
#include <atomic>
#include <new>

using Microsoft::WRL::ComPtr;

namespace
{
    std::atomic<long> moduleReferences{0};

    bool IsClipDiffReady() noexcept
    {
        const HANDLE event = OpenEventW(SYNCHRONIZE, FALSE, ClipDiffReadyEvent);
        if (!event) return false;
        const bool ready = WaitForSingleObject(event, 0) == WAIT_OBJECT_0;
        CloseHandle(event);
        return ready;
    }

    bool IsFilePair(IDataObject* data) noexcept
    {
        if (!data) return false;
        // The application consumes CF_HDROP. Do not offer an action it cannot receive.
        FORMATETC format{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
        if (data->QueryGetData(&format) != S_OK) return false;

        // IShellExtInit supplies the complete selection. No file contents or display names are requested.
        ComPtr<IShellItemArray> items;
        if (FAILED(SHCreateShellItemArrayFromDataObject(data, IID_PPV_ARGS(&items)))) return false;
        DWORD count = 0;
        if (FAILED(items->GetCount(&count)) || count != 2) return false;
        for (DWORD index = 0; index < count; ++index)
        {
            ComPtr<IShellItem> item;
            if (FAILED(items->GetItemAt(index, &item))) return false;
            SFGAOF attributes = 0;
            if (FAILED(item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER | SFGAO_STREAM, &attributes))) return false;
            if (!(attributes & SFGAO_FILESYSTEM)) return false;
            // ZIPs can be both folders and streams; physical directories are not streams.
            if ((attributes & SFGAO_FOLDER) && !(attributes & SFGAO_STREAM)) return false;
        }
        return true;
    }

    class ContextMenu final : public IShellExtInit, public IContextMenu
    {
        std::atomic<ULONG> references_{1};
        ComPtr<IDataObject> selection_;

        ~ContextMenu() { selection_.Reset(); --moduleReferences; }

    public:
        ContextMenu() { ++moduleReferences; }

        IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid == IID_IUnknown || iid == IID_IShellExtInit)
                *result = static_cast<IShellExtInit*>(this);
            else if (iid == IID_IContextMenu)
                *result = static_cast<IContextMenu*>(this);
            else return E_NOINTERFACE;
            AddRef();
            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return ++references_; }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG remaining = --references_;
            if (!remaining) delete this;
            return remaining;
        }

        IFACEMETHODIMP Initialize(PCIDLIST_ABSOLUTE, IDataObject* data, HKEY) override
        {
            selection_.Reset();
            if (IsClipDiffReady() && IsFilePair(data)) selection_ = data;
            return S_OK;
        }

        IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT position, UINT firstId, UINT lastId, UINT flags) override
        {
            if ((flags & CMF_DEFAULTONLY) || firstId > lastId || !selection_ || !IsClipDiffReady())
                return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);

            MENUITEMINFOW item{sizeof(item)};
            item.fMask = MIIM_ID | MIIM_STRING | MIIM_STATE;
            item.wID = firstId;
            item.fState = MFS_ENABLED;
            item.dwTypeData = const_cast<wchar_t*>(ClipDiffMenuLabel);
            if (!InsertMenuItemW(menu, position, TRUE, &item)) return HRESULT_FROM_WIN32(GetLastError());
            return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 1);
        }

        IFACEMETHODIMP GetCommandString(UINT_PTR id, UINT flags, UINT*, LPSTR buffer, UINT capacity) override
        {
            if (id != 0) return E_INVALIDARG;
            if (flags == GCS_VERBW) return StringCchCopyW(reinterpret_cast<LPWSTR>(buffer), capacity, ClipDiffVerb);
            if (flags == GCS_VERBA) return StringCchCopyA(buffer, capacity, ClipDiffVerbAnsi);
            if (flags == GCS_HELPTEXTW)
                return StringCchCopyW(reinterpret_cast<LPWSTR>(buffer), capacity, L"Compare the two selected files.");
            if (flags == GCS_HELPTEXTA) return StringCchCopyA(buffer, capacity, "Compare the two selected files.");
            if (flags == GCS_VALIDATEA || flags == GCS_VALIDATEW) return S_OK;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* command) override
        {
            if (!command || command->cbSize < sizeof(CMINVOKECOMMANDINFO)) return E_INVALIDARG;
            const wchar_t* unicodeVerb = nullptr;
            if (command->cbSize >= sizeof(CMINVOKECOMMANDINFOEX) && (command->fMask & CMIC_MASK_UNICODE))
            {
                const auto extended = reinterpret_cast<CMINVOKECOMMANDINFOEX*>(command);
                if (!IS_INTRESOURCE(extended->lpVerbW)) unicodeVerb = extended->lpVerbW;
            }
            const bool matches = unicodeVerb ? lstrcmpiW(unicodeVerb, ClipDiffVerb) == 0
                : IS_INTRESOURCE(command->lpVerb) ? LOWORD(reinterpret_cast<ULONG_PTR>(command->lpVerb)) == 0
                : lstrcmpiA(command->lpVerb, ClipDiffVerbAnsi) == 0;
            if (!matches) return E_INVALIDARG;

            // Consume the selection once and release it on every exit, including failed delivery.
            ComPtr<IDataObject> data;
            data.Swap(selection_);
            if (!IsClipDiffReady() || !IsFilePair(data.Get())) return E_FAIL;
            ComPtr<IDropTarget> target;
            HRESULT result = CoCreateInstance(CLSID_ClipDiffDropTarget, nullptr, CLSCTX_LOCAL_SERVER,
                IID_PPV_ARGS(&target));
            if (FAILED(result)) return result;

            DWORD effect = DROPEFFECT_COPY;
            result = target->DragEnter(data.Get(), MK_LBUTTON, POINTL{}, &effect);
            if (FAILED(result) || !(effect & DROPEFFECT_COPY))
            {
                target->DragLeave();
                return FAILED(result) ? result : E_FAIL;
            }
            effect = DROPEFFECT_COPY;
            return target->Drop(data.Get(), MK_LBUTTON, POINTL{}, &effect);
        }
    };

    class ClassFactory final : public IClassFactory
    {
        std::atomic<ULONG> references_{1};
        ~ClassFactory() { --moduleReferences; }
    public:
        ClassFactory() { ++moduleReferences; }
        IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return ++references_; }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            const ULONG remaining = --references_;
            if (!remaining) delete this;
            return remaining;
        }
        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** result) override
        {
            if (!result) return E_POINTER;
            *result = nullptr;
            if (outer) return CLASS_E_NOAGGREGATION;
            auto instance = new (std::nothrow) ContextMenu();
            if (!instance) return E_OUTOFMEMORY;
            const HRESULT status = instance->QueryInterface(iid, result);
            instance->Release();
            return status;
        }
        IFACEMETHODIMP LockServer(BOOL lock) override
        {
            if (lock) ++moduleReferences;
            else --moduleReferences;
            return S_OK;
        }
    };
}

STDAPI DllGetClassObject(REFCLSID classId, REFIID iid, void** result)
{
    if (!result) return E_POINTER;
    *result = nullptr;
    if (classId != CLSID_ClipDiffShellExtension) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) ClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    const HRESULT status = factory->QueryInterface(iid, result);
    factory->Release();
    return status;
}

STDAPI DllCanUnloadNow()
{
    return moduleReferences == 0 ? S_OK : S_FALSE;
}
