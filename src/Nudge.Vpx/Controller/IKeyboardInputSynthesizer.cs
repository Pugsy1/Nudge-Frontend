using Nudge.Core.Models;

namespace Nudge.Vpx.Controller;

/// <summary>
/// Synthesizes a keyboard key press or release, indistinguishable to whatever application has
/// focus from a real key on a real keyboard. Behind an interface so <see cref="ControllerInputSession"/>
/// is testable without actually injecting input into the real OS input queue.
/// </summary>
public interface IKeyboardInputSynthesizer
{
    void KeyDown(VirtualKey key);

    void KeyUp(VirtualKey key);
}
