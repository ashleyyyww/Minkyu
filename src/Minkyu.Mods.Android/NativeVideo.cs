using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Minkyu.Mods;

// VideoPlayer has native calls but no IL2CPP class in this build.
internal sealed class NativeVideo
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AddComponent(IntPtr self, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PointerGetter(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ActionCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PointerSetter(IntPtr self, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void IntSetter(IntPtr self, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BoolSetter(IntPtr self, [MarshalAs(UnmanagedType.I1)] bool value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FloatSetter(IntPtr self, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DoubleSetter(IntPtr self, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool BoolGetter(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double DoubleGetter(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint UIntGetter(IntPtr self);
    private static readonly Dictionary<string, Delegate> calls = new();
    private readonly Component component;
    private IntPtr Pointer => component.Pointer;

    private static T Resolve<T>(string method) where T : Delegate
    {
        if (calls.TryGetValue(method, out var cached)) return (T)cached;
        var pointer = IL2CPP.il2cpp_resolve_icall("UnityEngine.Video.VideoPlayer::" + method);
        if (pointer == IntPtr.Zero) throw new MissingMethodException("Unity video call unavailable: " + method);
        var call = Marshal.GetDelegateForFunctionPointer<T>(pointer);
        calls.Add(method, call);
        return call;
    }

    internal NativeVideo(GameObject owner)
    {
        var add = IL2CPP.il2cpp_resolve_icall("UnityEngine.GameObject::AddComponentInternal(System.String)");
        if (add == IntPtr.Zero) throw new MissingMethodException("Unity native component creation unavailable");
        var pointer = Marshal.GetDelegateForFunctionPointer<AddComponent>(add)(owner.Pointer, IL2CPP.ManagedStringToIl2Cpp("VideoPlayer"));
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Unity video component unavailable");
        component = new Component(pointer);
    }

    internal void Configure(string file, Camera camera, float opacity)
    {
        Resolve<BoolSetter>("set_playOnAwake(System.Boolean)")(Pointer, false);
        Resolve<IntSetter>("set_renderMode(UnityEngine.Video.VideoRenderMode)")(Pointer, (int)UnityEngine.Video.VideoRenderMode.APIOnly);
        Resolve<PointerSetter>("set_targetCamera(UnityEngine.Camera)")(Pointer, camera.Pointer);
        Resolve<FloatSetter>("set_targetCameraAlpha(System.Single)")(Pointer, opacity);
        Resolve<IntSetter>("set_audioOutputMode(UnityEngine.Video.VideoAudioOutputMode)")(Pointer, (int)UnityEngine.Video.VideoAudioOutputMode.None);
        Resolve<IntSetter>("set_aspectRatio(UnityEngine.Video.VideoAspectRatio)")(Pointer, (int)UnityEngine.Video.VideoAspectRatio.FitOutside);
        Resolve<BoolSetter>("set_skipOnDrop(System.Boolean)")(Pointer, true);
        Resolve<PointerSetter>("set_url(System.String)")(Pointer, IL2CPP.ManagedStringToIl2Cpp(file));
    }

    internal bool isPrepared => Resolve<BoolGetter>("get_isPrepared()")(Pointer);
    internal bool isPlaying => Resolve<BoolGetter>("get_isPlaying()")(Pointer);
    internal bool canSetTime => Resolve<BoolGetter>("get_canSetTime()")(Pointer);
    internal bool canSetPlaybackSpeed => Resolve<BoolGetter>("get_canSetPlaybackSpeed()")(Pointer);
    internal uint width => Resolve<UIntGetter>("get_width()")(Pointer);
    internal uint height => Resolve<UIntGetter>("get_height()")(Pointer);
    internal Texture Texture
    {
        get
        {
            var pointer = Resolve<PointerGetter>("get_texture()")(Pointer);
            return pointer == IntPtr.Zero ? null : new Texture(pointer);
        }
    }
    internal double time
    {
        get => Resolve<DoubleGetter>("get_time()")(Pointer);
        set => Resolve<DoubleSetter>("set_time(System.Double)")(Pointer, value);
    }
    internal float playbackSpeed { set => Resolve<FloatSetter>("set_playbackSpeed(System.Single)")(Pointer, value); }
    internal void Prepare() => Resolve<ActionCall>("Prepare()")(Pointer);
    internal void Play() => Resolve<ActionCall>("Play()")(Pointer);
    internal void Pause() => Resolve<ActionCall>("Pause()")(Pointer);
    internal void Stop()
    {
        if (component != null) Resolve<ActionCall>("Stop()")(Pointer);
    }
}
