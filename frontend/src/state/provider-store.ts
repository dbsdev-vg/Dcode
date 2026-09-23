import type {
  AIProvider,
  Provider,
  ProviderState,
} from "../types/dcode";

const state: ProviderState = {
  providers: [
    {
      id: "deepseek",
      name: "DeepSeek",
      status: "disconnected",
    },
    {
      id: "gemini",
      name: "Gemini",
      status: "disconnected",
    },
    {
      id: "chatgpt",
      name: "ChatGPT",
      status: "disconnected",
    },
  ],
  activeProvider: null,
};

export function getProviderState(): ProviderState {
  return state;
}

export function setActiveProvider(
  provider: AIProvider | null,
): void {
  state.activeProvider = provider;
}

export function updateProvider(
  providerId: AIProvider,
  updates: Partial<Provider>,
): void {
  const provider: Provider | undefined = state.providers.find(
    (item: Provider) => item.id === providerId,
  );

  if (!provider) {
    return;
  }

  Object.assign(provider, updates);
}