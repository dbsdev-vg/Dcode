import type { BeforeMount } from "@monaco-editor/react";

export const configureDCodeMonaco: BeforeMount = (monaco) => {
  monaco.editor.defineTheme("dcode-dark", { base: "vs-dark", inherit: true, rules: [
    { token: "comment", foreground: "68717E", fontStyle: "italic" }, { token: "keyword", foreground: "F06A66" },
    { token: "string", foreground: "A9D18E" }, { token: "number", foreground: "E7B87A" }, { token: "type", foreground: "79B8C8" },
    { token: "function", foreground: "D7DCE2" }, { token: "delimiter", foreground: "8D96A2" },
  ], colors: {
    "editor.background": "#0D1014", "editor.foreground": "#D7DCE2", "editorCursor.foreground": "#E53935",
    "editor.selectionBackground": "#E5393533", "editor.inactiveSelectionBackground": "#30374166", "editor.lineHighlightBackground": "#151A20",
    "editorLineNumber.foreground": "#4F5865", "editorLineNumber.activeForeground": "#AEB6C2", "editorIndentGuide.background1": "#232A32",
    "editorIndentGuide.activeBackground1": "#49515C", "editorBracketHighlight.foreground1": "#E97B77", "editorBracketHighlight.foreground2": "#79B8C8",
    "editorBracketHighlight.foreground3": "#D5B77A", "editorError.foreground": "#F05A55", "editorWarning.foreground": "#D5A94E",
    "minimap.background": "#0D1014", "scrollbarSlider.background": "#59627033", "scrollbarSlider.hoverBackground": "#69748266",
    "editorOverviewRuler.border": "#00000000",
  }});
  monaco.editor.defineTheme("dcode-light", { base: "vs", inherit: true, rules: [
    { token: "comment", foreground: "6B7280", fontStyle: "italic" }, { token: "keyword", foreground: "B32622" },
    { token: "string", foreground: "397446" }, { token: "number", foreground: "9A5D16" }, { token: "type", foreground: "176B82" },
  ], colors: { "editor.background": "#F8F9FA", "editor.foreground": "#252A31", "editorCursor.foreground": "#D32F2F", "editor.selectionBackground": "#E5393526", "editor.lineHighlightBackground": "#F0F2F4", "editorLineNumber.foreground": "#A1A7AF", "editorLineNumber.activeForeground": "#4A5058", "editorError.foreground": "#D32F2F", "editorWarning.foreground": "#A56A00", "minimap.background": "#F8F9FA", "editorOverviewRuler.border": "#00000000" }});

  const compilerOptions = { allowJs: true, allowNonTsExtensions: true, allowSyntheticDefaultImports: true, esModuleInterop: true, jsx: monaco.languages.typescript.JsxEmit.ReactJSX, module: monaco.languages.typescript.ModuleKind.ESNext, moduleResolution: monaco.languages.typescript.ModuleResolutionKind.NodeJs, resolveJsonModule: true, target: monaco.languages.typescript.ScriptTarget.ESNext };
  monaco.languages.typescript.typescriptDefaults.setCompilerOptions(compilerOptions);
  monaco.languages.typescript.javascriptDefaults.setCompilerOptions(compilerOptions);
  const diagnostics = { noSemanticValidation: true, noSyntaxValidation: false, diagnosticCodesToIgnore: [2307, 2503, 2792, 2875] };
  monaco.languages.typescript.typescriptDefaults.setDiagnosticsOptions(diagnostics);
  monaco.languages.typescript.javascriptDefaults.setDiagnosticsOptions(diagnostics);
};

export function editorLanguage(name: string) {
  const extension = name.split(".").pop()?.toLowerCase() ?? "";
  return ({ cs: "csharp", csproj: "xml", css: "css", html: "html", js: "javascript", json: "json", jsx: "javascript", md: "markdown", scss: "scss", sql: "sql", ts: "typescript", tsx: "typescript", xml: "xml", yaml: "yaml", yml: "yaml" } as Record<string, string>)[extension] ?? "plaintext";
}

export const modelPath = (path: string) => `file:///dcode/${path.replaceAll("\\", "/")}`;
