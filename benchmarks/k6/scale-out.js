// Replays a few fixed aggregation queries through the Aspire proxy, then sums upstream calls over every replica's /statistics.
import http from 'k6/http';
import { check } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://host.docker.internal:5181';
const replicaUrls = (__ENV.REPLICA_URLS || baseUrl).split(',').map((url) => url.trim()).filter(Boolean);
const headers = { Authorization: `Bearer ${__ENV.TOKEN}` };

// Few distinct queries keep the run under GitHub's unauthenticated search limit of 10 a minute, even with three cold replicas.
const queries = [
  'q=rust&city=Athens',
  'q=golang&city=Berlin',
  'q=dotnet&city=Paris',
];

export const options = {
  scenarios: {
    replay: {
      executor: 'constant-arrival-rate',
      // Below the per-caller token bucket (10 a second), so one token is never rate limited.
      rate: 5,
      timeUnit: '1s',
      duration: '30s',
      preAllocatedVUs: 5,
    },
  },
  thresholds: {
    checks: ['rate==1'],
  },
};

function aggregate(query) {
  return http.get(`${baseUrl}/api/v1/aggregation?${query}`, { headers, tags: { name: 'aggregation' } });
}

export function setup() {
  if (!__ENV.TOKEN) {
    throw new Error('Set TOKEN to a JWT with the aggregation:read and stats:read scopes.');
  }

  // One request per query first, so the measured window starts warm and replicas never race on a cold key.
  for (const query of queries) {
    check(aggregate(query), { 'warm-up is 200': (response) => response.status === 200 });
  }
}

export default function () {
  const query = queries[Math.floor(Math.random() * queries.length)];
  check(aggregate(query), { 'aggregation is 200': (response) => response.status === 200 });
}

export function teardown() {
  const totals = {};
  const responses = http.batch(replicaUrls.map((url) => ['GET', `${url}/api/v1/statistics`, null, { headers }]));
  for (const response of responses) {
    if (!check(response, { 'statistics is 200': (r) => r.status === 200 })) {
      continue;
    }
    for (const item of response.json('items') || []) {
      const total = totals[item.source] || { upstreamCalls: 0, cacheHits: 0 };
      totals[item.source] = {
        upstreamCalls: total.upstreamCalls + item.totalRequests,
        cacheHits: total.cacheHits + item.cacheHits,
      };
    }
  }

  const upstreamCalls = Object.values(totals).reduce((sum, total) => sum + total.upstreamCalls, 0);
  console.log(`replicas=${replicaUrls.length} upstreamCalls=${upstreamCalls} perSource=${JSON.stringify(totals)}`);
}
