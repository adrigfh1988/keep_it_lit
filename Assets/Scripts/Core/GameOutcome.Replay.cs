using UnityEngine;

/// <summary>
/// F120: the death replay hook. HandleCaught's death branch stops the recorder and sets _caughtByHunter; the two caught end screens (story and
/// DESCENT) call <see cref="TryShowReplay"/> with their root, which adds a <see cref="DeathReplayView"/> to it when the hunter killed the player,
/// <c>TerrorSettings.deathReplay</c> is on and the recorder holds at least 2 s of usable frames. A timeout, the loop Visitor and the Listener get none.
/// </summary>
public partial class GameOutcome
{
    private DeathReplayRecorder _replay;
    private TerrorSettings _replayTerror;

    /// <summary>F120: wired by MazeGenerator.SetUpAtmosphere. May stay null (no replay).</summary>
    public void BindReplay(DeathReplayRecorder recorder, TerrorSettings terror)
    {
        _replay = recorder;
        _replayTerror = terror;
    }

    private void TryShowReplay(Transform root, bool descent)
    {
        if (!_caughtByHunter || _replay == null || root == null) return;
        if (_replayTerror != null && !_replayTerror.deathReplay) return;
        if (_follower == null || _player == null) return;
        if (!_replay.TryGetWindow(6f, 2f, out ReplayFrame[] frames)) return;

        DeathReplayView view = root.gameObject.AddComponent<DeathReplayView>();
        view.Begin(frames, _follower, _player, _maze, descent);
    }
}
