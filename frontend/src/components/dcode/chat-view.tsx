"use client";

import { Badge, Button, Textarea } from "@dbs-studio/ui";
import { Bot, Globe2, MessageSquare, PlugZap, Send } from "lucide-react";

import { useWorkspace } from "../../state/workspace-context";

export function ChatView() {
  const { chatProviderSession, setActiveWorkspace } = useWorkspace();

  if (!chatProviderSession) {
    return (
      <section className="dcode-chat-page dcode-chat-page--empty">
        <div className="dcode-chat-empty">
          <span><MessageSquare size={24} /></span>
          <h1>Start a provider chat</h1>
          <p>Select a connected provider account before starting a conversation.</p>
          <Button icon={<PlugZap size={15} />} onClick={() => setActiveWorkspace("providers")}>Choose provider</Button>
        </div>
      </section>
    );
  }

  return (
    <section className="dcode-chat-page">
      <header className="dcode-chat-header">
        <div className="dcode-chat-header__identity">
          <span><Bot size={19} /></span>
          <div><h1>New chat</h1><p>Provider session selected for this conversation</p></div>
        </div>
        <div className="dcode-chat-connection">
          <Globe2 size={15} />
          <div><strong>{chatProviderSession.providerName}</strong><span>{chatProviderSession.accountName}</span></div>
          <Badge variant="success">Connected</Badge>
        </div>
      </header>

      <div className="dcode-chat-body">
        <div className="dcode-chat-welcome">
          <span><Bot size={23} /></span>
          <h2>Chat with {chatProviderSession.providerName}</h2>
          <p>DCode will use the browser account <strong>{chatProviderSession.accountName}</strong> for this conversation.</p>
          <Badge variant="outline">Browser provider</Badge>
        </div>
      </div>

      <footer className="dcode-chat-composer">
        <Textarea disabled placeholder="Message sending will be enabled when the chat backend is connected." aria-label="Chat message" />
        <Button icon={<Send size={15} />} disabled>Send</Button>
      </footer>
    </section>
  );
}
