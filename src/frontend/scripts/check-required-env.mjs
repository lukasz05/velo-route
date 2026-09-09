import nextEnv from "@next/env";
const { loadEnvConfig } = nextEnv;

loadEnvConfig(process.cwd());

const required = ["NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY"];

const missing = required.filter((key) => !process.env[key]);

if (missing.length > 0) {
  console.error(
    `Missing required environment variable(s): ${missing.join(", ")}`,
  );
  process.exit(1);
}
