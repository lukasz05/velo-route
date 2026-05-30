import type { NextConfig } from "next";
import path from "path";
import fs from "fs";

// If a local CA cert is present (e.g. corporate MITM proxy), trust it in development.
// Place your CA in PEM format at src/frontend/local-ca.pem (gitignored).
if (process.env.NODE_ENV !== 'production') {
  const localCa = path.join(__dirname, 'local-ca.pem');
  if (fs.existsSync(localCa)) {
    process.env.NODE_EXTRA_CA_CERTS = localCa;
  }
}

const nextConfig: NextConfig = {
  output: 'standalone',
};

export default nextConfig;
