using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;


using System.IO;
namespace SamplePlugin;

using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;

using System;
using System.Runtime.InteropServices;

public unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private static readonly CameraManager* CamManager = (CameraManager*)FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance();

    private const string CommandName = "/pmycommand";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private bool inRelevantDialogueEvent;
    private IGameObject? currentTarget;
    private RestoreInfo restoreCamInfo;

    struct RestoreInfo
    {
       public bool stored;
       public int mode;
       public float minFOV;
       public float maxFOV;
       public float currentFOV;
    }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [SamplePlugin] ===A cool log message from Sample Plugin===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");

        Plugin.Condition.ConditionChange += ConditionChange;

        AddonLifecycle.RegisterListener(AddonEvent.PreShow, "Talk", OnTalkAddonRefresh);

        
    }

    private void OnTalkAddonRefresh(AddonEvent type, AddonArgs args)
    {
        if (!inRelevantDialogueEvent)
        {
            return;
        }

        if(type == AddonEvent.PreShow)
        {      
            // Get the current active target
            IGameObject? ct = TargetManager.Target;

            if (ct != null &&
                (ct.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc ||
                ct.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc))
            {     
                if(currentTarget == null)
                {
                    if (restoreCamInfo.stored == false)
                    {
                        restoreCamInfo = new RestoreInfo
                        {
                            stored = true,
                            mode = Camera->mode,
                            minFOV = Camera->minFoV,
                            maxFOV = Camera->maxFoV,
                            currentFOV = Camera->currentFoV,
                        };
                    }

                    Marshal.StructureToPtr(0, CameraMode, true);
                    Marshal.StructureToPtr(0.175f, MinFOV, true);
                    Marshal.StructureToPtr(0.175f, MaxFOV, true);

                    currentTarget = ct;
                }

                if(ct != currentTarget)
                {
                    currentTarget = ct;
                    string npcName = currentTarget.Name.TextValue;
                    Plugin.PluginLog.Information("Talking To: {argtype}", npcName);
                }
            }
        }

        //if(type == AddonEvent.PostHide)
        //{
        //    Plugin.PluginLog.Information("Talking To: {argtype}", npcName);
        //}
    }

    void ConditionChange(ConditionFlag flag, bool value)
    { 
        if(flag == ConditionFlag.OccupiedInQuestEvent)
        {
            inRelevantDialogueEvent = value;
            Plugin.PluginLog.Information("In Quest Event: {value}", value);

            if(value == false)
            {
                currentTarget = null;

                if (restoreCamInfo.stored)
                {
                    Marshal.StructureToPtr(restoreCamInfo.mode, CameraMode, true);
                    Marshal.StructureToPtr(restoreCamInfo.minFOV, MinFOV, true);
                    Marshal.StructureToPtr(restoreCamInfo.maxFOV, MaxFOV, true);
                    Marshal.StructureToPtr(restoreCamInfo.currentFOV, CurrentFOV, true);
                }

                restoreCamInfo = default;
            }
        }
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        Plugin.Condition.ConditionChange -= ConditionChange;

        AddonLifecycle.UnregisterListener(AddonEvent.PreShow, "Talk", OnTalkAddonRefresh);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    private static GameCamera* Camera => CamManager->worldCamera;
    public static IntPtr CurrentFOV => (IntPtr)(&Camera->currentFoV);
    public static IntPtr MinFOV => (IntPtr)(&Camera->minFoV);
    public static IntPtr MaxFOV => (IntPtr)(&Camera->maxFoV);
    public static IntPtr CameraMode => (IntPtr)(&Camera->mode);

    /// <summary>
    /// https://github.com/UnknownX7/Hypostasis/blob/master/Game/Structures/CameraManager.cs
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct CameraManager
    {
        [FieldOffset(0x0)] public FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager CS;
        [FieldOffset(0x0)] public GameCamera* worldCamera;
        [FieldOffset(0x8)] public GameCamera* idleCamera;
        [FieldOffset(0x10)] public GameCamera* menuCamera;
        [FieldOffset(0x18)] public GameCamera* spectatorCamera;

        public static bool Validate() => true;
    }

    /// <summary>
    /// https://github.com/UnknownX7/Hypostasis/blob/master/Game/Structures/GameCamera.cs
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct GameCamera
    {
        [FieldOffset(0x0)] public nint* vtbl;
        [FieldOffset(0x60)] public float x;
        [FieldOffset(0x64)] public float y;
        [FieldOffset(0x68)] public float z;
        [FieldOffset(0x90)] public float lookAtX; // Position that the camera is focused on (Actual position when zoom is 0)
        [FieldOffset(0x94)] public float lookAtY;
        [FieldOffset(0x98)] public float lookAtZ;
        [FieldOffset(0x124)] public float currentZoom; // 6
        [FieldOffset(0x128)] public float minZoom; // 1.5
        [FieldOffset(0x12C)] public float maxZoom; // 20
        [FieldOffset(0x130)] public float currentFoV; // 0.78
        [FieldOffset(0x134)] public float minFoV; // 0.69
        [FieldOffset(0x138)] public float maxFoV; // 0.78
        [FieldOffset(0x13C)] public float addedFoV; // 0
        [FieldOffset(0x140)] public float currentHRotation; // -pi -> pi, default is pi
        [FieldOffset(0x144)] public float currentVRotation; // -0.349066
        [FieldOffset(0x148)] public float hRotationDelta;
        [FieldOffset(0x158)] public float minVRotation; // -1.483530, should be -+pi/2 for straight down/up but camera breaks so use -+1.569
        [FieldOffset(0x15C)] public float maxVRotation; // 0.785398 (pi/4)
        [FieldOffset(0x170)] public float tilt;
        [FieldOffset(0x180)] public int mode; // Camera mode? (0 = 1st person, 1 = 3rd person, 2+ = weird controller mode? cant look up/down)
        [FieldOffset(0x184)] public int controlType; // 0 first person, 1 legacy, 2 standard, 4 talking to npc in first person (with option enabled), 5 talking to npc (with option enabled), 3/6 ???
        [FieldOffset(0x18C)] public float interpolatedZoom;
        [FieldOffset(0x1A0)] public float transition; // Seems to be related to the 1st <-> 3rd camera transition
        [FieldOffset(0x1C0)] public float viewX;
        [FieldOffset(0x1C4)] public float viewY;
        [FieldOffset(0x1C8)] public float viewZ;
        [FieldOffset(0x1F4)] public byte isFlipped; // 1 while holding the keybind
        [FieldOffset(0x22C)] public float interpolatedY;
        [FieldOffset(0x234)] public float lookAtHeightOffset; // No idea what to call this (0x230 is the interpolated value)
        [FieldOffset(0x238)] public byte resetLookatHeightOffset; // No idea what to call this
        [FieldOffset(0x240)] public float interpolatedLookAtHeightOffset;
        [FieldOffset(0x2C0)] public byte lockPosition;
        [FieldOffset(0x2D4)] public float lookAtY2;

        public readonly bool IsHRotationOffset => mode == isFlipped;
        public readonly float GameObjectHRotation => !IsHRotationOffset ? (currentHRotation > 0 ? currentHRotation - MathF.PI : currentHRotation + MathF.PI) : currentHRotation;

        public static bool Validate() => true;
    }
}
