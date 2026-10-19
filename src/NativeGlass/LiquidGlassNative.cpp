#include <windows.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <d2d1effects.h>
#include <windows.graphics.effects.interop.h>
#include <windows.ui.composition.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <memory>
using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Effects;
using namespace Windows::UI::Composition;
using EffectInterop = ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop;

struct BlurEffect : implements<BlurEffect, IGraphicsEffect, IGraphicsEffectSource, EffectInterop>
{
    IGraphicsEffectSource source{nullptr};
    hstring name{L"GlassBlur"};
    BlurEffect(IGraphicsEffectSource const& input) : source(input) {}
    hstring Name() { return name; }
    void Name(hstring const& value) { name = value; }
    HRESULT __stdcall GetEffectId(GUID* value) noexcept override { *value = CLSID_D2D1GaussianBlur; return S_OK; }
    HRESULT __stdcall GetNamedPropertyMapping(LPCWSTR, UINT*, ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING*) noexcept override { return E_NOTIMPL; }
    HRESULT __stdcall GetPropertyCount(UINT* value) noexcept override { *value = 3; return S_OK; }
    HRESULT __stdcall GetSourceCount(UINT* value) noexcept override { *value = 1; return S_OK; }
    HRESULT __stdcall GetSource(UINT index, ABI::Windows::Graphics::Effects::IGraphicsEffectSource** value) noexcept override
    {
        if (index != 0) return E_INVALIDARG;
        *value = reinterpret_cast<ABI::Windows::Graphics::Effects::IGraphicsEffectSource*>(detach_abi(source.as<IGraphicsEffectSource>()));
        return S_OK;
    }
    HRESULT __stdcall GetProperty(UINT index, ABI::Windows::Foundation::IPropertyValue** value) noexcept override
    {
        try {
            IPropertyValue property{nullptr};
            if (index == 0) property = PropertyValue::CreateSingle(9.0f).as<IPropertyValue>();
            else if (index == 1) property = PropertyValue::CreateUInt32(1).as<IPropertyValue>();
            else if (index == 2) property = PropertyValue::CreateUInt32(1).as<IPropertyValue>();
            else return E_INVALIDARG;
            *value = reinterpret_cast<ABI::Windows::Foundation::IPropertyValue*>(detach_abi(property));
            return S_OK;
        } catch (...) { return to_hresult(); }
    }
};

struct GlassRender
{
    HWND front{}, window{};
    Windows::System::DispatcherQueueController dispatcher{nullptr};
    Compositor compositor{nullptr};
    Desktop::DesktopWindowTarget target{nullptr};
    SpriteVisual visual{nullptr};
    CompositionRoundedRectangleGeometry geometry{nullptr};
    ContainerVisual root{nullptr};
    static LRESULT CALLBACK Procedure(HWND hwnd, UINT message, WPARAM w, LPARAM l)
    {
        if (message == WM_NCHITTEST) return HTTRANSPARENT;
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        if (message == WM_ERASEBKGND) return 1;
        return DefWindowProcW(hwnd, message, w, l);
    }
    GlassRender(HWND owner) : front(owner) {}
    void Initialize()
    {
        init_apartment(apartment_type::single_threaded);
        if (!Windows::System::DispatcherQueue::GetForCurrentThread())
        {
            DispatcherQueueOptions options{sizeof(options), DQTYPE_THREAD_CURRENT, DQTAT_COM_STA};
            check_hresult(CreateDispatcherQueueController(options, reinterpret_cast<PDISPATCHERQUEUECONTROLLER*>(put_abi(dispatcher))));
        }
        WNDCLASSW wc{};
        wc.lpfnWndProc = Procedure;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"CodexGpuGlassSurface";
        RegisterClassW(&wc);
        window = CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
            wc.lpszClassName, L"", WS_POPUP | WS_DISABLED, 0, 0, 1, 1, nullptr, nullptr, wc.hInstance, nullptr);
        if (!window) throw_last_error();
        BOOL enabled = TRUE;
        check_hresult(DwmSetWindowAttribute(window, 17, &enabled, sizeof(enabled)));
        compositor = Compositor();
        auto interop = compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
        check_hresult(interop->CreateDesktopWindowTarget(window, TRUE, reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
        auto effect = make<BlurEffect>(CompositionEffectSourceParameter(L"Backdrop"));
        auto factory = compositor.CreateEffectFactory(effect);
        auto brush = factory.CreateBrush();
        brush.SetSourceParameter(L"Backdrop", compositor.CreateHostBackdropBrush());
        visual = compositor.CreateSpriteVisual();
        visual.Brush(brush);
        geometry = compositor.CreateRoundedRectangleGeometry();
        visual.Clip(compositor.CreateGeometricClip(geometry));
        root = compositor.CreateContainerVisual();
        root.Children().InsertAtTop(visual);
        target.Root(root);
    }
    void Sync(float scale)
    {
        RECT rect{};
        if (!GetWindowRect(front, &rect)) return;
        float width = static_cast<float>(rect.right - rect.left);
        float height = static_cast<float>(rect.bottom - rect.top);
        geometry.Size({width, height});
        geometry.CornerRadius({24 * scale, 24 * scale});
        visual.Size({width, height});
        root.Size({width, height});
        SetWindowPos(window, front, rect.left, rect.top, static_cast<int>(width), static_cast<int>(height), SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
    ~GlassRender()
    {
        try { if (target) { target.Root(nullptr); target.Close(); } if (compositor) compositor.Close(); } catch (...) {}
        if (window) DestroyWindow(window);
        try { if (dispatcher) dispatcher.ShutdownQueueAsync(); } catch (...) {}
    }
};
extern "C" __declspec(dllexport) void* __cdecl GlassCreate(HWND front, HRESULT* error)
{
    try { auto object = std::make_unique<GlassRender>(front); object->Initialize(); *error = S_OK; return object.release(); }
    catch (...) { *error = to_hresult(); return nullptr; }
}
extern "C" __declspec(dllexport) void __cdecl GlassSync(GlassRender* object, float scale)
{
    try { if (object) object->Sync(scale); } catch (...) {}
}
extern "C" __declspec(dllexport) void __cdecl GlassDestroy(GlassRender* object) { delete object; }
