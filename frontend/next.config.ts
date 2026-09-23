import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  transpilePackages: [
    "@dbs-studio/ui",
    "@dbs-studio/theme",
  ],

  turbopack: {
    root: "../../../",
  },
};

export default nextConfig;