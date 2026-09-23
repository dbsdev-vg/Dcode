"use client";

import {
  Badge, Button, Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle, IconButton, Input, Select, Textarea,
} from "@dbs-studio/ui";
import {
  Bot, Globe2, MessageSquare, MoreHorizontal, Plus, Search, Send, X,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";

import { useWorkspace } from "../../state/workspace-context";
import type { ChatConversation, ChatMessage } from "../../types/dcode";

const serverUrl = "http://localhost:5283";

interface ConnectedBrowserProfile {
  id: string;
  name: string;
  connected: boolean;
}

export function ConversationChatView() {
  const { chatProviderSession, closeChat, setActiveWorkspace, startChat } = useWorkspace();
  const [conversations, setConversations] = useState<ChatConversation[]>([]);
  const [profiles, setProfiles] = useState<ConnectedBrowserProfile[]>([]);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState("");
  const [pendingContent, setPendingContent] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [newConversationOpen, setNewConversationOpen] = useState(false);
  const [selectedProfileId, setSelectedProfileId] = useState("");
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      fetch(`${serverUrl}/api/chats`),
      fetch(`${serverUrl}/api/providers/browser/deepseek/profiles`),
    ])
      .then(async ([chatsResponse, profilesResponse]) => {
        if (!chatsResponse.ok) throw new Error(`Unable to load chats (HTTP ${chatsResponse.status})`);
        if (!profilesResponse.ok) throw new Error(`Unable to load provider accounts (HTTP ${profilesResponse.status})`);
        const nextConversations = await chatsResponse.json() as ChatConversation[];
        const nextProfiles = (await profilesResponse.json() as ConnectedBrowserProfile[]).filter((profile) => profile.connected);
        return { nextConversations, nextProfiles };
      })
      .then(({ nextConversations, nextProfiles }) => {
        setConversations(nextConversations);
        setProfiles(nextProfiles);
        setSelectedProfileId(chatProviderSession?.profileId ?? nextProfiles[0]?.id ?? "");
      })
      .catch((caught) => setError(caught instanceof Error ? caught.message : "Unable to load chats"))
      .finally(() => setLoading(false));
  }, [chatProviderSession?.profileId]);

  const filtered = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return query
      ? conversations.filter((chat) => chat.title.toLocaleLowerCase().includes(query) || chat.accountName.toLocaleLowerCase().includes(query))
      : conversations;
  }, [conversations, search]);

  const activeConversation = conversations.find((chat) => chat.id === chatProviderSession?.conversationId) ?? null;

  useEffect(() => {
    if (!chatProviderSession?.conversationId) {
      setMessages([]);
      return;
    }
    fetch(`${serverUrl}/api/chats/${chatProviderSession.conversationId}/messages`)
      .then(async (response) => {
        if (!response.ok) throw new Error(`Unable to load messages (HTTP ${response.status})`);
        return response.json() as Promise<ChatMessage[]>;
      })
      .then(setMessages)
      .catch((caught) => setError(caught instanceof Error ? caught.message : "Unable to load messages"));
  }, [chatProviderSession?.conversationId]);

  function openConversation(chat: ChatConversation) {
    startChat({
      conversationId: chat.id,
      profileId: chat.browserProfileId,
      providerId: "deepseek",
      providerName: "DeepSeek",
      accountName: chat.accountName,
      transport: "browser",
    });
  }

  async function createConversation() {
    if (!selectedProfileId) return;
    setCreating(true); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/chats`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ browserProfileId: selectedProfileId }),
      });
      if (!response.ok) throw new Error(await readError(response));
      const chat = (await response.json()) as ChatConversation;
      setConversations((current) => [chat, ...current]);
      setNewConversationOpen(false);
      openConversation(chat);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "Unable to create chat"); }
    finally { setCreating(false); }
  }

  async function sendMessage() {
    const content = draft.trim();
    if (!content || !activeConversation || sending) return;
    setDraft(""); setPendingContent(content); setSending(true); setError(null);
    try {
      const response = await fetch(`${serverUrl}/api/chats/${activeConversation.id}/messages`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ content }),
      });
      if (!response.ok) throw new Error(await readError(response));
      const responseBody = await response.text();
      if (!responseBody) throw new Error("DCode.Server returned an empty chat response.");
      const exchange = JSON.parse(responseBody) as { conversation: ChatConversation; messages: ChatMessage[] };
      setMessages(exchange.messages);
      setConversations((current) => [exchange.conversation, ...current.filter((chat) => chat.id !== exchange.conversation.id)]);
    } catch (caught) {
      setDraft(content);
      setError(caught instanceof Error ? caught.message : "Unable to send message");
    } finally { setPendingContent(null); setSending(false); }
  }

  return (
    <section className="dcode-conversation-workspace">
      <aside className="dcode-conversation-sidebar">
        <div className="dcode-conversation-sidebar__header">
          <div><span className="dcode-page-eyebrow">Conversations</span><h1>Chat</h1></div>
          <IconButton label="New chat" icon={<Plus size={16} />} variant="ghost" disabled={creating} onClick={() => setNewConversationOpen(true)} />
        </div>
        <div className="dcode-conversation-search"><Search size={14} /><Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search chats" aria-label="Search chats" /></div>
        <div className="dcode-conversation-list">
          {loading ? <p>Loading chats...</p> : null}
          {!loading && filtered.length === 0 ? <p>{search ? "No matching chats." : "No conversations yet."}</p> : null}
          {filtered.map((chat) => (
            <button type="button" className={chat.id === activeConversation?.id ? "is-active" : ""} key={chat.id} onClick={() => openConversation(chat)}>
              <MessageSquare size={15} />
              <span><strong>{chat.title}</strong><small>{chat.accountName} · {formatUpdated(chat.updatedUtc)}</small></span>
            </button>
          ))}
        </div>
      </aside>

      <div className="dcode-conversation-main">
        {activeConversation && chatProviderSession ? (
          <>
            <header className="dcode-chat-header">
              <div className="dcode-chat-header__identity"><span><Bot size={19} /></span><div><h1>{activeConversation.title}</h1><p>Local conversation · Remote conversation pending</p></div></div>
              <div className="dcode-chat-header__actions">
                <div className="dcode-chat-connection"><Globe2 size={15} /><div><strong>DeepSeek</strong><span>{chatProviderSession.accountName}</span></div><Badge variant="success">Connected</Badge></div>
                <IconButton label="Conversation actions" icon={<MoreHorizontal size={16} />} variant="ghost" disabled />
                <IconButton label="Close conversation" icon={<X size={17} />} variant="ghost" onClick={closeChat} />
              </div>
            </header>
            {error ? <div className="dcode-provider-error" role="alert">{error}</div> : null}
            <div className="dcode-chat-body">
              {messages.length === 0 && !pendingContent ? (
                <div className="dcode-chat-welcome"><span className="dcode-chat-hero-icon"><Bot size={23} /></span><h2>New conversation</h2><p>This local chat is permanently bound to <strong>{chatProviderSession.accountName}</strong>. Its DeepSeek conversation will be created with the first message.</p><Badge variant="outline">No messages yet</Badge></div>
              ) : (
                <div className="dcode-chat-messages">
                  {messages.map((message) => <ChatMessageBubble message={message} key={message.id} />)}
                  {pendingContent ? <ChatMessageBubble message={{ id: "pending-user", chatId: activeConversation.id, role: "user", content: pendingContent, providerMessageId: null, createdUtc: new Date().toISOString() }} /> : null}
                  {sending ? <div className="dcode-chat-message dcode-chat-message--assistant"><span className="dcode-chat-message__avatar"><Bot size={15} /></span><div><small>DeepSeek</small><p className="dcode-chat-thinking">Thinking...</p></div></div> : null}
                </div>
              )}
            </div>
            <footer className="dcode-chat-composer"><Textarea value={draft} onChange={(event) => setDraft(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); void sendMessage(); } }} disabled={sending} placeholder="Message DeepSeek" aria-label="Chat message" /><Button icon={<Send size={15} />} disabled={!draft.trim() || sending} onClick={() => void sendMessage()}>{sending ? "Sending..." : "Send"}</Button></footer>
          </>
        ) : (
          <div className="dcode-chat-empty">
            <span className="dcode-chat-hero-icon"><MessageSquare size={24} /></span><h1>{conversations.length ? "Select a conversation" : "Start a provider chat"}</h1>
            <p>{conversations.length ? "Choose a saved chat from the conversation list." : "Select a connected provider account to create your first conversation."}</p>
            <Button icon={conversations.length ? <MessageSquare size={15} /> : <Plus size={15} />} onClick={() => conversations[0] ? openConversation(conversations[0]) : setNewConversationOpen(true)}>{conversations.length ? "Open recent chat" : "New conversation"}</Button>
          </div>
        )}
      </div>

      <Dialog open={newConversationOpen} onOpenChange={(open) => { if (!creating) setNewConversationOpen(open); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>New conversation</DialogTitle>
            <DialogDescription>Choose the provider connection that will own this conversation. It cannot be silently switched after messages are sent.</DialogDescription>
          </DialogHeader>

          <div className="dcode-new-conversation-form">
            <label htmlFor="chat-provider">Provider</label>
            <Select id="chat-provider" value="deepseek-browser" disabled><option value="deepseek-browser">DeepSeek · Browser</option></Select>

            <label htmlFor="chat-account">Connected account</label>
            <Select id="chat-account" value={selectedProfileId} onChange={(event) => setSelectedProfileId(event.target.value)} disabled={!profiles.length}>
              {!profiles.length ? <option value="">No connected accounts</option> : null}
              {profiles.map((profile) => <option value={profile.id} key={profile.id}>{profile.name}</option>)}
            </Select>

            <label htmlFor="chat-model">Model</label>
            <Select id="chat-model" value="provider-default" disabled><option value="provider-default">Provider default · DeepSeek Web</option></Select>

            <div className="dcode-browser-setup-note"><Globe2 size={16} /><span>The first message will create the matching conversation in DeepSeek and permanently bind its remote ID to this DCode chat.</span></div>
          </div>

          <DialogFooter>
            {!profiles.length ? <Button variant="ghost" onClick={() => { setNewConversationOpen(false); setActiveWorkspace("providers"); }}>Manage providers</Button> : null}
            <Button variant="ghost" disabled={creating} onClick={() => setNewConversationOpen(false)}>Cancel</Button>
            <Button disabled={!selectedProfileId || creating} onClick={() => void createConversation()}>{creating ? "Creating..." : "Create conversation"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}

function ChatMessageBubble({ message }: { message: ChatMessage }) {
  const assistant = message.role === "assistant";
  return (
    <article className={`dcode-chat-message ${assistant ? "dcode-chat-message--assistant" : "dcode-chat-message--user"}`}>
      <span className="dcode-chat-message__avatar">{assistant ? <Bot size={15} /> : "Y"}</span>
      <div><small>{assistant ? "DeepSeek" : "You"}</small><p>{message.content}</p></div>
    </article>
  );
}

function formatUpdated(value: string) {
  return new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric" }).format(new Date(value));
}

async function readError(response: Response) {
  try { const body = (await response.json()) as { error?: string }; return body.error ?? `Request failed (HTTP ${response.status})`; }
  catch { return `Request failed (HTTP ${response.status})`; }
}
