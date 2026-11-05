import { database, defineRailway, github, postgres, project, service, volume } from "railway/iac";

// Orken ERP on Railway (.railway/README.md). This file manages `backend` and `main` in both environments, and the
// staging databases. It is a `partial`: the production Postgres, Redis and volumes are not listed for production, and
// apply never touches or deletes them. Variables are unmanaged: secrets live in Railway, never here.
export const partial = "nexoerp";

const REGION = "europe-west4-drams3a";
const REPO = "eliasandraade/nexo-erp";

export default defineRailway((ctx) => {
  // Staging deploys every merge to master. Production deploys only the `production` branch, which moves forward on
  // purpose and only to a commit whose `master-gate` check passed (docs/engineering/staging-e-promocao.md).
  const production = ctx.isEnvironment("production");
  if (!production && !ctx.isEnvironment("staging")) throw new Error("Only the staging and production environments are defined here");
  const branch = production ? "production" : "master";

  const backend = service("backend", {
    // checkSuites: Railway deploys a commit only after its GitHub CI passed.
    source: github(REPO, { branch, checkSuites: true, rootDirectory: "/nexo-backend" }),
    build: { buildEnvironment: "V3", builder: "RAILPACK" },
    healthcheck: "/health",
    healthcheckTimeout: 120,
    replicas: { [REGION]: 1 },
    deploy: { sleepApplication: true },
    networking: { privateNetworkEndpoint: "nexo-erp" },
    // Custom domains exist only in production for now; staging keeps its Railway domains until the DNS records exist.
    ...(production ? { domains: ["api.erp.orken.com.br"] } : {}),
  });

  const main = service("main", {
    source: github(REPO, { branch, checkSuites: true, rootDirectory: "/nexo-main" }),
    build: { buildEnvironment: "V3", builder: "RAILPACK" },
    healthcheck: "/",
    healthcheckTimeout: 60,
    replicas: { [REGION]: 1 },
    deploy: { sleepApplication: true },
    ...(production ? { domains: ["erp.orken.com.br", "www.orken.com.br"] } : {}),
  });

  if (production) {
    return project("nexoerp", { variables: { managed: false }, resources: [backend, main] });
  }

  // Staging has its own, empty databases. Nothing is copied from production; the API seeds synthetic data on start.
  const alerts = { usage: { "100": {}, "80": {}, "95": {} } };
  const Postgres = postgres("Postgres", { region: REGION });
  Postgres.networking = { privateNetworkEndpoint: "postgres" };
  const postgresVolume = volume("postgres-volume", { alerts, allowOnlineResize: true, region: REGION, sizeMB: 5000 });
  // The same image as production. The default image of redis() runs without root and cannot start this command.
  const Redis = database("Redis", "redis", { image: "redis:8.2.10", output: "REDIS_URL", defaultMountPath: "/bitnami", region: REGION });
  Redis.deploy = {
    startCommand:
      '/bin/sh -c "rm -rf $RAILWAY_VOLUME_MOUNT_PATH/lost+found/ && exec docker-entrypoint.sh redis-server --requirepass $REDIS_PASSWORD --save 60 1 --dir $RAILWAY_VOLUME_MOUNT_PATH --bind 0.0.0.0 ::"',
  };
  Redis.networking = { privateNetworkEndpoint: "redis" };
  const redisVolume = volume("redis-volume", { alerts, allowOnlineResize: true, region: REGION, sizeMB: 500 });

  return project("nexoerp", {
    variables: { managed: false },
    resources: [Postgres, postgresVolume, Redis, redisVolume, backend, main],
  });
});
