import PageIntro from "../components/PageIntro";

const globalHotkeys = [
  { keys: "Alt + `", action: "Capture with rectangle select" },
  { keys: "Alt + Shift + `", action: "Run OCR on a new selection" },
  { keys: "Alt + C", action: "Open the color picker" },
  { keys: "Customizable", action: "QR and barcode scanning" },
  { keys: "Customizable", action: "Sticker maker" },
  { keys: "Customizable", action: "Ruler" },
  { keys: "Customizable", action: "GIF or video recording" },
  { keys: "Customizable", action: "Fullscreen capture" },
  { keys: "Customizable", action: "Active window capture" },
  { keys: "Customizable", action: "Scrolling capture" },
];

const annotationHotkeys = [
  { keys: "1", action: "Select tool" },
  { keys: "2", action: "Arrow" },
  { keys: "3", action: "Curved arrow" },
  { keys: "4", action: "Text" },
  { keys: "5", action: "Highlight" },
  { keys: "6", action: "Blur" },
  { keys: "7", action: "Step marker" },
  { keys: "8", action: "Freehand draw" },
  { keys: "9", action: "Line" },
  { keys: "0", action: "Ruler" },
  { keys: "-", action: "Rectangle" },
  { keys: "=", action: "Circle" },
  { keys: "[", action: "Emoji" },
  { keys: "]", action: "Eraser" },
];

const captureHotkeys = [
  { keys: "Ctrl + Z", action: "Undo the last annotation" },
  { keys: "Delete", action: "Delete the selected annotation" },
  { keys: "Escape", action: "Cancel capture or close the popup" },
  { keys: "Enter", action: "Confirm text input" },
  { keys: "Tab", action: "Reopen the emoji picker while placing emoji" },
  { keys: "Shift + drag", action: "Constrain to a square or straight line" },
];

function HotkeyTable({
  title,
  description,
  rows,
}: {
  title: string;
  description: string;
  rows: Array<{ keys: string; action: string }>;
}) {
  return (
    <section className="panel p-6">
      <div className="space-y-2">
        <h2 className="section-title">{title}</h2>
        <p className="section-copy">{description}</p>
      </div>

      <div className="table-shell mt-5 rounded-[1.1rem] border border-[var(--line)]">
        <table>
          <thead>
            <tr>
              <th>Shortcut</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.action}>
                <td>
                  <kbd className="kbd-pill">{row.keys}</kbd>
                </td>
                <td>{row.action}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

export default function Hotkeys() {
  return (
    <div>
      <PageIntro
        eyebrow="Hotkey reference"
        title="The defaults are fast. Everything stays remappable."
        description="Yoink ships with practical defaults, but the point is consistency rather than lock-in. If these do not fit your keyboard habits, adjust them in Settings."
      />

      <HotkeyTable
        title="Global hotkeys"
        description="These fire even when Yoink is in the background."
        rows={globalHotkeys}
      />
      <HotkeyTable
        title="Annotation tools"
        description="These are the quick switches you use while the capture overlay is open."
        rows={annotationHotkeys}
      />
      <HotkeyTable
        title="During capture"
        description="These cover the small edits and escapes that keep the capture flow moving."
        rows={captureHotkeys}
      />
    </div>
  );
}
