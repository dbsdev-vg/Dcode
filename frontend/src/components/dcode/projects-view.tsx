"use client";

import { useEffect, useState } from "react";
import { ArrowRight, FolderGit2, FolderOpen, Plus } from "lucide-react";
import { Button } from "@dbs-studio/ui";

import { useProject } from "../../state/project-context";
import { useWorkspace } from "../../state/workspace-context";
import { DCodeBrand } from "../brand/dcode-brand";
type ProjectInfo = {
    id: string;
    name: string;
    path: string;
};

type DCodeHostMessage =
    | {
        type: "project-selected";
        path: string;
    }
    | {
        type: "open-project-cancelled";
    }
    | {
        type: "host-error";
        message: string;
    };

declare global {
    interface Window {
        chrome?: {
            webview?: {
                postMessage: (message: unknown) => void;

                addEventListener: (
                    type: "message",
                    listener: (
                        event: MessageEvent<DCodeHostMessage>,
                    ) => void,
                ) => void;

                removeEventListener: (
                    type: "message",
                    listener: (
                        event: MessageEvent<DCodeHostMessage>,
                    ) => void,
                ) => void;
            };
        };
    }
}

export function ProjectsView() {
    const {
        projects,
        addProject,
        restoreProjects,
        setActiveProject,
    } = useProject();

    const {
        setActiveWorkspace,
    } = useWorkspace();

    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        async function restoreProjectState() {
            try {
                const [projectsResponse, activeResponse] = await Promise.all([
                    fetch("http://localhost:5283/api/projects"),
                    fetch("http://localhost:5283/api/projects/active"),
                ]);

                if (!projectsResponse.ok || !activeResponse.ok) {
                    throw new Error("Unable to restore projects");
                }

                const persistedProjects =
                    await projectsResponse.json() as ProjectInfo[];
                const activeProject =
                    await activeResponse.json() as ProjectInfo | null;

                restoreProjects(persistedProjects);
                setActiveProject(activeProject);
            } catch {
                // The project picker remains usable if persistence is offline.
            }
        }

        void restoreProjectState();
    }, [restoreProjects, setActiveProject]);

    // useEffect...

    // -------------------------------------------
    // Listen for messages coming FROM DCode.Host
    // -------------------------------------------

    useEffect(() => {
        const webview = window.chrome?.webview;

        if (!webview) {
            return;
        }

        const handleMessage = async (
            event: MessageEvent<DCodeHostMessage>,
        ) => {
            const message = event.data;

            if (message.type === "open-project-cancelled") {
                setLoading(false);
                return;
            }

            if (message.type === "host-error") {
                setError(message.message);
                setLoading(false);
                return;
            }

            if (message.type !== "project-selected") {
                return;
            }

            try {
                const response = await fetch(
                    "http://localhost:5283/api/projects/open",
                    {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/json",
                        },
                        body: JSON.stringify({
                            path: message.path,
                        }),
                    },
                );

                if (!response.ok) {
                    throw new Error(`HTTP ${response.status}`);
                }

                const project: ProjectInfo =
                    await response.json();

                addProject(project);
                setActiveProject(project);
                setActiveWorkspace("files");
            } catch (error) {
                setError(
                    error instanceof Error
                        ? error.message
                        : "Failed to open project",
                );
            } finally {
                setLoading(false);
            }
        };

        webview.addEventListener(
            "message",
            handleMessage,
        );

        return () => {
            webview.removeEventListener(
                "message",
                handleMessage,
            );
        };
    }, [addProject, setActiveProject, setActiveWorkspace]);

    // -------------------------------------------
    // Send message TO DCode.Host
    // -------------------------------------------

    function openProject() {
        const webview = window.chrome?.webview;

        if (!webview) {
            setError(
                "DCode native host is not available.",
            );
            return;
        }

        setLoading(true);
        setError(null);

        webview.postMessage({
            type: "open-project",
        });
    }

    function enterProject(project: ProjectInfo) {
        setActiveProject(project);
        setActiveWorkspace("files");
    }

    // -------------------------------------------
    // UI
    // -------------------------------------------

    return (
        <section className="dcode-workspace-page">
            <div className="dcode-workspace-page__header">
                <div>
                    <span className="dcode-page-eyebrow">Workspace</span>
                    <h1>Projects</h1>
                    <p>Open and manage local repositories.</p>
                </div>

                <Button
                    icon={<Plus size={16} />}
                    loading={loading}
                    onClick={openProject}
                >
                    Open project
                </Button>
            </div>

            {error && (
                <div className="dcode-project-error">
                    {error}
                </div>
            )}

            {projects.length === 0 ? (
                <div className="dcode-empty-projects">
                    <DCodeBrand
                        className="dcode-empty-projects__brand"
                        size={72}
                        showTagline
                        showSupportingLine
                    />

                    <h2>No projects yet</h2>

                    <p>
                        Open a local folder to start working
                        with DCode.
                    </p>
                </div>
            ) : (
                <div className="dcode-project-grid">
                    {projects.map((project) => (
                        <button
                            type="button"
                            className="dcode-project-card"
                            key={project.id}
                            onClick={() => enterProject(project)}
                        >
                            <div className="dcode-project-card__topline">
                                <span className="dcode-project-card__icon">
                                    <FolderGit2 size={22} />
                                </span>

                                <span className="dcode-project-card__badge">
                                    Local repository
                                </span>
                            </div>

                            <div className="dcode-project-card__body">
                                <strong>{project.name}</strong>
                                <span>Open the project workspace</span>
                            </div>

                            <div className="dcode-project-card__footer">
                                <span
                                    className="dcode-project-card__path"
                                    title={project.path}
                                >
                                    <FolderOpen size={14} />
                                    <span>{project.path}</span>
                                </span>

                                <ArrowRight
                                    className="dcode-project-card__arrow"
                                    size={18}
                                />
                            </div>
                        </button>
                    ))}
                </div>
            )}
        </section>
    );
}
