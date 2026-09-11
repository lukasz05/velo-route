/** @type {import('dependency-cruiser').IConfiguration} */
module.exports = {
  forbidden: [
    {
      name: "no-circular",
      severity: "warn",
      comment: "Circular imports make change impact hard to reason about.",
      from: {},
      to: { circular: true },
    },
    {
      name: "no-orphans",
      severity: "info",
      comment: "Modules nothing imports — candidates for dead code.",
      from: { orphan: true, pathNot: ["\\.test\\.[jt]sx?$", "\\.d\\.ts$", "next-env\\.d\\.ts$"] },
      to: {},
    },
    {
      name: "types-are-foundation",
      severity: "error",
      comment: "src/types must not depend on app or components — it's the base layer.",
      from: { path: "^src/types" },
      to: { path: "^src/(app|components)" },
    },
    {
      name: "lib-no-components",
      severity: "warn",
      comment: "src/lib is shared logic/utilities; it should not reach into UI components.",
      from: { path: "^src/lib" },
      to: { path: "^src/components" },
    },
    {
      name: "api-routes-no-components",
      severity: "error",
      comment: "src/app/api/** are backend-proxy route handlers, not UI — they must not import React components.",
      from: { path: "^src/app/api" },
      to: { path: "^src/components" },
    },
  ],
  options: {
    doNotFollow: { path: "node_modules" },
    tsPreCompilationDeps: true,
    tsConfig: { fileName: "tsconfig.json" },
    enhancedResolveOptions: {
      exportsFields: ["exports"],
      conditionNames: ["import", "require", "node", "default"],
    },
    reporterOptions: {
      dot: {
        collapsePattern: "node_modules/[^/]+",
      },
    },
  },
};
