"use client";

import { Children, isValidElement, type ReactNode, useState } from "react";
import ReactMarkdown from "react-markdown";
import rehypeHighlight from "rehype-highlight";
import remarkGfm from "remark-gfm";
import { Check, Copy } from "lucide-react";

function textContent(node: ReactNode): string {
  if (typeof node === "string" || typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(textContent).join("");
  if (isValidElement<{ children?: ReactNode }>(node)) return textContent(node.props.children);
  return "";
}

function CodeBlock({ children }: { children?: ReactNode }) {
  const [copied, setCopied] = useState(false);
  const code = Children.toArray(children)[0];
  const className = isValidElement<{ className?: string }>(code) ? code.props.className ?? "" : "";
  const language = className.match(/language-([\w-]+)/)?.[1] ?? "text";
  const source = textContent(code).replace(/\n$/, "");

  async function copy() {
    await navigator.clipboard.writeText(source);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1500);
  }

  return <div className="dcode-chat-code">
    <div className="dcode-chat-code__bar"><span>{language}</span><button type="button" onClick={() => void copy()}>{copied ? <Check size={12} /> : <Copy size={12} />}{copied ? "Copied" : "Copy"}</button></div>
    <pre>{children}</pre>
  </div>;
}

export function MarkdownMessage({ content }: { content: string }) {
  return <div className="dcode-chat-markdown">
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      rehypePlugins={[rehypeHighlight]}
      skipHtml
      components={{
        pre: ({ children }) => <CodeBlock>{children}</CodeBlock>,
        a: ({ href, children }) => <a href={href} target="_blank" rel="noreferrer">{children}</a>,
      }}
    >{content}</ReactMarkdown>
  </div>;
}
