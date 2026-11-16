import { LumiBarClient } from './lumibar-client.mjs';
const endpoint = process.argv[2] ?? process.env.LUMIBAR_ENDPOINT;
if (!endpoint) throw new Error('Usage: node smoke.mjs <frontend-endpoint.json>');
const client = new LumiBarClient();
try {
  const hello = await client.connect(endpoint);
  const state = await client.request('state.get');
  const spectrum = await client.request('spectrum.get');
  const hints = await client.request('windows.hints');
  console.log(JSON.stringify({ hello, state, spectrum, hints }, null, 2));
} finally { client.close(); }
