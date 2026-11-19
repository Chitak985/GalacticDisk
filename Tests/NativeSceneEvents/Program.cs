using System;

// Standalone .NET Framework check against the game's actual Assembly-CSharp.dll.
internal sealed class Program
{
    private int calls;
    private void Loaded(GameScenes scene) { calls++; }
    private static void StaticLoaded(GameScenes scene) { }
    private static int Main()
    {
        try
        {
            var listener = new Program();
            foreach (string name in new[] { "onGameSceneLoadRequested", "onLevelWasLoaded", "onLevelWasLoadedGUIReady" })
            {
                var sceneEvent = new EventData<GameScenes>(name);
                bool rejected = false;
                try { sceneEvent.Add(StaticLoaded); }
                catch (NullReferenceException) { rejected = true; }
                if (!rejected) throw new Exception("Expected the installed KSP event API to reject static callbacks.");
                int before = listener.calls;
                sceneEvent.Add(listener.Loaded); sceneEvent.Fire(GameScenes.SPACECENTER);
                if (listener.calls != before+1) throw new Exception("Instance callback did not fire.");
                sceneEvent.Remove(listener.Loaded); sceneEvent.Fire(GameScenes.FLIGHT);
                if (listener.calls != before+1) throw new Exception("Instance callback was not removed.");
            }
            Console.WriteLine("PASS - actual KSP EventData: static callback failure reproduced; instance callbacks add, fire and remove for all three scene event signatures.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}
