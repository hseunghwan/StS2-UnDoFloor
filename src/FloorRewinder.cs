using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace StS2UnDoFloor;

/// <summary>
/// Restores a <see cref="FloorCheckpoint"/>: tears the current run scene down and loads the checkpoint's save exactly
/// the way "Continue" on the main menu does (see FileDropHandler / NGame.LoadRun in the game).
/// SetUpSavedSingleplayer also rewrites the run save on disk, so quitting afterwards keeps the rewound state.
/// </summary>
public static class FloorRewinder
{
    public static bool IsBusy { get; private set; }

    public static bool CanRewind()
    {
        RunManager runManager = RunManager.Instance;
        return !IsBusy
            && runManager.IsInProgress
            && runManager.NetService.Type == NetGameType.Singleplayer;
    }

    public static async Task RewindTo(FloorCheckpoint checkpoint)
    {
        if (!CanRewind())
        {
            Log.Warn($"[{UnDoFloorMod.Id}] Cannot rewind right now (busy={IsBusy}).");
            return;
        }
        NGame game = NGame.Instance ?? throw new InvalidOperationException("NGame.Instance is null.");
        RunManager runManager = RunManager.Instance;
        IsBusy = true;
        bool fadedOut = false;
        try
        {
            Log.Info($"[{UnDoFloorMod.Id}] Rewinding to {checkpoint}.");
            ActBrowser.Reset();
            SerializableRun save = checkpoint.LoadSave();

            await game.Transition.FadeOut();
            fadedOut = true;
            runManager.CleanUp();

            RunState runState = RunState.FromSerializable(save);
            await runManager.SetUpSavedSingleplayer(runState, save);
            game.ReactionContainer.InitializeNetworking(new NetSingleplayerGameService());
            runManager.MapDrawingsToLoad = save.MapDrawings;
            await game.LoadRun(runState, save.PreFinishedRoom);

            await game.Transition.FadeIn();
            fadedOut = false;
            await RestoreMapMarker(game, runState);
            Log.Info($"[{UnDoFloorMod.Id}] Rewind complete.");
        }
        catch (Exception e)
        {
            Log.Error($"[{UnDoFloorMod.Id}] Rewind to {checkpoint} failed:\n{e}");
            if (fadedOut && GodotObject.IsInstanceValid(game))
            {
                try
                {
                    await game.Transition.FadeIn(0.2f);
                }
                catch (Exception fadeException)
                {
                    Log.Error($"[{UnDoFloorMod.Id}] Failed to fade back in after a failed rewind:\n{fadeException}");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>A freshly built map screen has no marker until told where the player is.</summary>
    private static async Task RestoreMapMarker(NGame game, RunState runState)
    {
        IReadOnlyList<MapCoord> visited = runState.VisitedMapCoords;
        if (visited.Count == 0)
        {
            return;
        }
        await game.GetTree().Root.AwaitProcessFrame();
        NMapScreen.Instance?.InitMarker(visited[visited.Count - 1]);
    }
}
