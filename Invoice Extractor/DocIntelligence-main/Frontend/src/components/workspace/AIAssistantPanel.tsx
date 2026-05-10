import { motion } from "framer-motion";
import { useState, useEffect, useRef } from "react";
import { Bot, Send, Sparkles, Loader2 } from "lucide-react";
import { initialMessages, aiQuickActions, wellPlanAiQuickActions, type ChatMessage } from "@/data/workspaceData";
import { extractionChat, isDemoMode } from "@/services/api";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { Components } from "react-markdown";

/** GFM tables + readable layout (pipes render as real HTML tables, not raw text). */
const chatMarkdownComponents: Components = {
  table: ({ children }) => (
    <div className="my-3 w-full max-w-full overflow-x-auto rounded-lg border border-border/50 bg-secondary/10 shadow-inner">
      <table className="w-full min-w-[280px] border-collapse text-left text-[13px] leading-snug">
        {children}
      </table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-secondary/50">{children}</thead>,
  th: ({ children }) => (
    <th className="border border-border/45 px-2.5 py-2 text-left text-xs font-semibold uppercase tracking-wide text-foreground">
      {children}
    </th>
  ),
  td: ({ children }) => (
    <td className="border border-border/35 px-2.5 py-1.5 align-top text-foreground/95">{children}</td>
  ),
  tbody: ({ children }) => <tbody className="[&_tr:nth-child(even)]:bg-secondary/15">{children}</tbody>,
  p: ({ children }) => <p className="mb-2 last:mb-0">{children}</p>,
  ul: ({ children }) => <ul className="my-2 list-disc pl-4 space-y-1">{children}</ul>,
  ol: ({ children }) => <ol className="my-2 list-decimal pl-4 space-y-1">{children}</ol>,
  code: ({ className, children, ...props }) => {
    const isBlock = className?.includes("language-");
    if (isBlock) {
      return (
        <code className={`${className ?? ""} block overflow-x-auto rounded-md bg-secondary/40 p-2 text-[12px] font-mono`} {...props}>
          {children}
        </code>
      );
    }
    return (
      <code className="rounded bg-secondary/50 px-1 py-0.5 font-mono text-[12px]" {...props}>
        {children}
      </code>
    );
  },
  pre: ({ children }) => <pre className="my-2 overflow-x-auto rounded-lg border border-border/40 bg-secondary/30 p-2 text-[12px]">{children}</pre>,
};

/** When set (after extraction), user questions go to /api/v1/extraction/chat (JSON context only). */
export interface ExtractionChatContext {
  request_id?: string;
  data?: Record<string, unknown> | null;
  document_type?: string;
  source_file?: string;
}

interface AIAssistantPanelProps {
  narrations?: string[];
  isProcessing?: boolean;
  summaryMessage?: ChatMessage | null;
  chatContext?: ExtractionChatContext | null;
}

export function AIAssistantPanel({
  narrations = [],
  isProcessing = false,
  summaryMessage,
  chatContext,
}: AIAssistantPanelProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [chatLoading, setChatLoading] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const addedNarrations = useRef<number>(0);

  useEffect(() => {
    if (narrations.length > addedNarrations.current) {
      const newOnes = narrations.slice(addedNarrations.current);
      addedNarrations.current = narrations.length;
      setMessages((prev) => [
        ...prev,
        ...newOnes.map((text, i) => ({
          id: `narr-${Date.now()}-${i}`,
          role: "ai" as const,
          content: text,
          timestamp: new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false }),
        })),
      ]);
    }
  }, [narrations]);

  const completeSummaryAdded = useRef(false);
  useEffect(() => {
    if (!isProcessing && narrations.length > 0 && !completeSummaryAdded.current) {
      completeSummaryAdded.current = true;
      setTimeout(() => {
        const finalMsg = summaryMessage || initialMessages[0];
        setMessages((prev) => [
          ...prev,
          { ...finalMsg, id: `final-${Date.now()}`, timestamp: new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false }) },
        ]);
      }, 600);
    }
  }, [isProcessing, narrations, summaryMessage]);

  useEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }, [messages, chatLoading]);

  const canUseBackendChat =
    !isProcessing &&
    chatContext &&
    (Boolean(chatContext.request_id) || Boolean(chatContext.data && Object.keys(chatContext.data).length > 0));

  const docTypeNorm = (chatContext?.document_type ?? "").toLowerCase().replace(/-/g, "_");
  const isWellPlan = docTypeNorm === "well_plan" || docTypeNorm === "wellplan";
  const quickActions = isWellPlan ? wellPlanAiQuickActions : aiQuickActions;

  const sendMessage = async () => {
    if (!input.trim() || chatLoading) return;
    const ts = new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false });
    const userMsg: ChatMessage = { id: `u-${Date.now()}`, role: "user", content: input.trim(), timestamp: ts };
    const question = input.trim();
    setMessages((prev) => [...prev, userMsg]);
    setInput("");

    if (isProcessing) {
      setMessages((prev) => [
        ...prev,
        {
          id: `a-${Date.now()}`,
          role: "ai",
          content: "I'm still extracting the document. Ask again once extraction finishes.",
          timestamp: ts,
        },
      ]);
      return;
    }

    if (canUseBackendChat && chatContext) {
      setChatLoading(true);
      try {
        const { reply } = await extractionChat({
          request_id: chatContext.request_id || undefined,
          message: question,
          data: chatContext.data ?? undefined,
          document_type: chatContext.document_type,
          source_file: chatContext.source_file,
        });
        setMessages((prev) => [
          ...prev,
          {
            id: `a-${Date.now()}`,
            role: "ai",
            content: reply || "(empty response)",
            timestamp: new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false }),
          },
        ]);
      } catch (e) {
        const err = e instanceof Error ? e.message : String(e);
        setMessages((prev) => [
          ...prev,
          {
            id: `a-${Date.now()}`,
            role: "ai",
            content: `**Could not reach the chat service.**\n\n${err}\n\n${isDemoMode() ? "Demo mode: ensure the API is running and the Vite proxy can reach it, or only `data` was missing from the request." : ""}`,
            timestamp: new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false }),
          },
        ]);
      } finally {
        setChatLoading(false);
      }
      return;
    }

    setMessages((prev) => [
      ...prev,
      {
        id: `a-${Date.now()}`,
        role: "ai",
        content:
          "Extraction data is not loaded yet. Finish extraction on the server, then try again — answers use **only the extracted JSON**, not the original PDF.",
        timestamp: new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false }),
      },
    ]);
  };

  return (
    <motion.div initial={{ opacity: 0, x: 16 }} animate={{ opacity: 1, x: 0 }} transition={{ duration: 0.4, delay: 0.3 }} className="glass-panel flex flex-col h-full overflow-hidden">
      <div className="px-4 py-3 border-b border-border/50 flex items-center gap-2.5">
        <div className="p-1.5 rounded-lg bg-primary/10"><Bot className="h-3.5 w-3.5 text-primary" /></div>
        <h2 className="text-sm font-semibold text-foreground">AI Assistant</h2>
        <div className="ml-auto flex items-center gap-1.5">
          {isProcessing ? (
            <><Loader2 className="h-3 w-3 text-primary animate-spin" /><span className="text-[10px] text-primary font-medium">Processing</span></>
          ) : chatLoading ? (
            <><Loader2 className="h-3 w-3 text-primary animate-spin" /><span className="text-[10px] text-primary font-medium">Thinking…</span></>
          ) : (
            <>
              <span className="status-dot-live" />
              <span className="text-[10px] text-muted-foreground" title="Answers use extracted JSON only, not the PDF">
                {isWellPlan ? "Well plan (JSON)" : "JSON context"}
              </span>
            </>
          )}
        </div>
      </div>

      <div ref={scrollRef} className="flex-1 overflow-y-auto px-3 py-3 space-y-3 custom-scrollbar">
        {messages.length === 0 && isProcessing && (
          <div className="flex items-center gap-2 text-muted-foreground/50 py-4 justify-center">
            <Loader2 className="h-4 w-4 animate-spin text-primary/50" /><span className="text-xs">Waiting for analysis to begin…</span>
          </div>
        )}
        {messages.map((msg) => (
          <motion.div key={msg.id} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.2 }}
            className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
            <div className={`max-w-[min(92%,42rem)] rounded-2xl px-3.5 py-2.5 text-[13px] leading-relaxed ${
              msg.role === "user" ? "bg-primary/15 text-foreground rounded-br-md" : "glass-panel-bright rounded-bl-md"
            }`}>
              {msg.role === "ai" ? (
                <div className="prose prose-sm prose-invert max-w-none [&_strong]:text-foreground [&_a]:text-primary [&_a]:underline">
                  <ReactMarkdown remarkPlugins={[remarkGfm]} components={chatMarkdownComponents}>
                    {msg.content}
                  </ReactMarkdown>
                </div>
              ) : (<p>{msg.content}</p>)}
              <p className="text-[10px] text-muted-foreground/50 mt-1.5">{msg.timestamp}</p>
            </div>
          </motion.div>
        ))}
        {chatLoading && (
          <motion.div
            initial={{ opacity: 0, y: 4 }}
            animate={{ opacity: 1, y: 0 }}
            className="flex justify-start"
            aria-live="polite"
            aria-busy="true"
          >
            <div className="flex max-w-[min(92%,42rem)] items-center gap-3 rounded-2xl rounded-bl-md border border-primary/25 bg-primary/5 px-4 py-3 shadow-[0_0_0_1px_hsl(var(--primary)/0.08)]">
              <Loader2 className="h-5 w-5 shrink-0 animate-spin text-primary" />
              <div className="min-w-0">
                <p className="text-sm font-medium text-foreground">Working on your answer…</p>
                <p className="text-[11px] text-muted-foreground">Using extracted JSON only (this may take a few seconds)</p>
              </div>
            </div>
          </motion.div>
        )}
      </div>

      {!isProcessing && (
        <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ delay: 0.3 }} className="px-3 py-2 flex flex-wrap gap-1.5">
          {quickActions.map((action) => (
            <button key={action} onClick={() => setInput(action)}
              className="px-2.5 py-1 rounded-full text-[11px] font-medium bg-secondary/60 text-muted-foreground hover:text-foreground hover:bg-secondary transition-colors btn-glow">
              <Sparkles className="h-2.5 w-2.5 inline mr-1" />{action}
            </button>
          ))}
        </motion.div>
      )}

      <div className="px-3 pb-3">
        <div className="flex items-center gap-2 glass-panel-bright rounded-xl px-3 py-2">
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && !e.shiftKey && (e.preventDefault(), void sendMessage())}
            placeholder={
              isProcessing
                ? "Wait for extraction…"
                : isWellPlan
                  ? "Ask about wells, casing, fluids, formations, risks (extracted JSON only)…"
                  : "Ask about the extracted data (JSON only, not the full PDF)…"
            }
            disabled={chatLoading}
            className="flex-1 bg-transparent text-sm text-foreground placeholder:text-muted-foreground/50 outline-none disabled:opacity-50" />
          <button
            type="button"
            onClick={() => void sendMessage()}
            disabled={chatLoading}
            className="p-1.5 rounded-lg bg-primary/15 text-primary hover:bg-primary/25 transition-colors disabled:opacity-50"
          >
            <Send className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>
    </motion.div>
  );
}
