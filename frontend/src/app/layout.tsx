import type { Metadata } from "next";
import "@dbs-studio/theme/styles.css";
import "@dbs-studio/ui/styles.css";
import "./globals.css";
import { WorkspaceProvider } from "@/state/workspace-context";
import { ProjectProvider } from "@/state/project-context";

export const metadata: Metadata = {
  title: "DCode",
  description: "Your development workspace, from code to production.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" data-product="dcode" className="dark">
      <body>
        <WorkspaceProvider>
          <ProjectProvider>
            {children}
          </ProjectProvider>
        </WorkspaceProvider>
      </body>
    </html>
  );
}
