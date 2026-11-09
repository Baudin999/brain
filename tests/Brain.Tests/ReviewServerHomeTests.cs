namespace Brain.Tests;

/// <summary>
/// Home's sky in a real headless Chromium: while the view pans, the painted sky is moved by a CSS transform and nothing
/// is painted, and once the pan lets go the sky is painted sharp at the new view and the transform clears. On a dark
/// theme, with the glow and halos painted, a running spark paints only the small box around it, on a canvas the size
/// of the view that moves with the sky, and with no spark the sparks' canvas has no pixels. It needs a
/// Chromium-family browser and is skipped, which a run shows, without one.
/// </summary>
public sealed partial class ReviewServerTests
{
    [WindowFact]
    public async Task HomesSkyMovesByATransformWhileItPansAndIsPaintedOnceItSettles()
    {
        const string front = "kind: idea\nsubjects: []\nnamed-by: user\nborn: 2026-10-03\n";
        brain.WriteNode("personal", "tea", front, "The user drinks tea");
        brain.WriteNode("global", "meadow", front, "Grass grows first", "- for -> personal:tea\n");

        await InWindow(async window =>
        {
            await window.Eval(Drag).ConfigureAwait(false);
            await window.Eval("showInbox(':home')").ConfigureAwait(false);
            await window.Until("sky.painted !== null", "true").ConfigureAwait(false);
            var moved = await window.Eval("""
                (() => {
                  window.paintedAt = sky.painted.at;
                  window.press(40, 0);
                  const shift = new DOMMatrix(sky.canvas.style.transform);
                  const still = sky.painted.at === window.paintedAt;
                  return `${shift.a} ${shift.e.toFixed(1)} ${shift.f.toFixed(1)} ${still} ${sky.spark.width}`;
                })()
                """).ConfigureAwait(false);
            Assert.Equal("1 40.0 0.0 true 0", moved);

            await window.Eval("window.release()").ConfigureAwait(false);
            var settled = "`${sky.canvas.style.transform}|${sky.painted.at > window.paintedAt}`";
            await window.Until(settled, "|true").ConfigureAwait(false);

            var sparked = await window.Eval("""
                (() => {
                  sky.dark = true;
                  sky.stillKey = null;
                  sky.data.edges.forEach((edge) => { edge.travelled = true; });
                  paintSky();
                  const seconds = performance.now() / 1000;
                  sky.sparks.forEach((spark) => { spark.phase = 1 - ((seconds / spark.every) % 1); });
                  stepSparks();
                  const box = sky.sparkBoxes[0];
                  const small = box.width < 100 && box.height < 100;
                  const wide = sky.spark.style.width === `${sky.wrap.clientWidth}px`;
                  const high = sky.spark.style.height === `${sky.wrap.clientHeight}px`;
                  window.press(40, 20);
                  const margin = skyMargin(sky.wrap.clientWidth, sky.wrap.clientHeight);
                  const wrap = sky.wrap.getBoundingClientRect();
                  const painted = sky.canvas.getBoundingClientRect();
                  const sparks = sky.spark.getBoundingClientRect();
                  const alignedX = Math.abs(painted.left + margin.x - sparks.left) < 0.5;
                  const alignedY = Math.abs(painted.top + margin.y - sparks.top) < 0.5;
                  window.release();
                  const shown = `${sky.sparks.length} ${sky.sparkBoxes.length} ${small} ${wide && high}`;
                  return `${shown} ${alignedX && alignedY} ${sparks.left - wrap.left}`;
                })()
                """).ConfigureAwait(false);
            Assert.Equal("1 1 true true true 40", sparked);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Alt+R is Home's key to show the whole sky, and the image viewer's to show the whole image. While the viewer is open
    /// it takes both Alt+R and Esc and Home hears neither: the sky stays where it was, and a pinned tip stays pinned when
    /// the viewer opens under the pointer. Once the viewer is closed, Alt+R shows the whole sky again.
    /// </summary>
    [WindowFact]
    public async Task HomesAltRBelongsToTheImageViewerWhileItIsOpen()
    {
        const string front = "kind: idea\nsubjects: []\nnamed-by: user\nborn: 2026-10-03\n";
        brain.WriteNode("personal", "tea", front, "The user drinks tea");
        brain.WriteNode("global", "meadow", front, "Grass grows first", "- for -> personal:tea\n");

        await InWindow(async window =>
        {
            await window.Eval("showInbox(':home')").ConfigureAwait(false);
            await window.Until("sky.painted !== null", "true").ConfigureAwait(false);
            await window.Eval("window.whole = { ...sky.view }; sky.view = { ...sky.view, x: sky.view.x + 100 }; sky.sticky = true; sky.tip.classList.add('sticky')").ConfigureAwait(false);

            await window.Eval(OpenViewer).ConfigureAwait(false);
            await window.Until("String(state.imageViewer !== null)", "true").ConfigureAwait(false);
            await window.Eval(TipLeaves).ConfigureAwait(false);
            await window.Eval(AltR).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            Assert.Equal("true", await window.Eval("String(sky.sticky && sky.view.x === window.whole.x + 100)").ConfigureAwait(false));

            await window.Eval(Escape).ConfigureAwait(false);
            await window.Until("String(state.imageViewer === null)", "true").ConfigureAwait(false);
            await window.Eval(TipLeaves).ConfigureAwait(false);
            Assert.Equal("false", await window.Eval("String(sky.sticky)").ConfigureAwait(false));

            await window.Eval(AltR).ConfigureAwait(false);
            await window.Until("String(Math.abs(sky.view.x - window.whole.x) < 0.5)", "true").ConfigureAwait(false);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }).ConfigureAwait(false);
    }

    private const string OpenViewer = """
        (() => {
          const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
          svg.setAttribute('viewBox', '0 0 10 10');
          sky.tip.append(svg);
          svg.dispatchEvent(new CustomEvent('open-image', { bubbles: true, detail: { node: svg } }));
        })()
        """;

    /// <summary>The pointer leaves the tip for something outside the sky, as it does when the viewer opens under it.</summary>
    private const string TipLeaves = "sky.tip.dispatchEvent(new MouseEvent('mouseleave', { relatedTarget: document.body }))";

    private const string AltR = "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'r', code: 'KeyR', altKey: true, bubbles: true, cancelable: true }))";

    private const string Escape = "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true, cancelable: true }))";

    /// <summary>A drag on the sky from its middle: press moves it a distance with the button held, release lets go.</summary>
    private const string Drag = """
        (() => {
          const middle = () => {
            const box = sky.wrap.getBoundingClientRect();
            return [box.left + box.width / 2, box.top + box.height / 2];
          };
          window.press = (dx, dy) => {
            const [x, y] = middle();
            const down = { button: 0, clientX: x, clientY: y, bubbles: true };
            sky.wrap.dispatchEvent(new MouseEvent('mousedown', down));
            sky.wrap.dispatchEvent(new MouseEvent('mousemove', { clientX: x + dx, clientY: y + dy, bubbles: true }));
          };
          window.release = () => window.dispatchEvent(new MouseEvent('mouseup', { button: 0, bubbles: true }));
        })()
        """;
}
