import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { Components } from "react-markdown";
import { CheckSquare, Square } from "lucide-react";
import { openExternal } from "@/lib/services/window";

const components: Components = {
  table: ({ children }) => <table className="w-full">{children}</table>,
  th: ({ children }) => (
    <th className="border p-1 border-border">{children}</th>
  ),
  td: ({ children }) => (
    <td className="border p-1 border-border">{children}</td>
  ),
  a: ({ href, children }) => (
    <a
      href={href}
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
        if (href) openExternal(href);
      }}
      className="text-blue-400 hover:underline"
    >
      {children}
    </a>
  ),
  strong: ({ children }) => (
    <strong className="font-semibold text-foreground/80">{children}</strong>
  ),
  em: ({ children }) => <em>{children}</em>,
  code: ({ children, className }) => {
    // Block code has a className like "language-xxx"
    if (className) {
      return (
        <code className="block bg-muted/50 rounded px-1.5 py-1 text-[10px] font-mono whitespace-pre overflow-x-auto my-0.5">
          {children}
        </code>
      );
    }
    return (
      <code className="bg-muted/50 rounded px-1 py-0.5 text-[10px] font-mono">
        {children}
      </code>
    );
  },
  pre: ({ children }) => <>{children}</>,
  h1: ({ children }) => (
    <div className="font-semibold text-foreground/80 mt-1 first:mt-0">
      {children}
    </div>
  ),
  h2: ({ children }) => (
    <div className="font-semibold text-foreground/80 mt-1 first:mt-0">
      {children}
    </div>
  ),
  h3: ({ children }) => (
    <div className="font-semibold text-foreground/80 mt-1 first:mt-0">
      {children}
    </div>
  ),
  hr: () => <hr className="border-t border-border my-1" />,
  ul: ({ children, className }) => (
    <ul className={className?.includes("contains-task-list") ? "list-none ml-1" : "list-disc list-inside ml-1"}>{children}</ul>
  ),
  ol: ({ children }) => (
    <ol className="list-decimal list-inside ml-1">{children}</ol>
  ),
  li: ({ children }) => (
    <li>
      <span className="inline-flex items-center">{children}</span>
    </li>
  ),
  p: ({ children }) => (
    <p className="my-0.5 first:mt-0 last:mb-0">{children}</p>
  ),
  input: ({ checked }) =>
    checked ? (
      <CheckSquare className="size-4 mr-1" />
    ) : (
      <Square className="size-4 mr-1" />
    ),
};

interface MarkdownContentProps {
  content: string;
}

export function MarkdownContent({ content }: MarkdownContentProps) {
  return (
    <div className="text-[11px] text-muted-foreground mt-0.5">
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
        {content}
      </ReactMarkdown>
    </div>
  );
}
