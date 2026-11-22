# Quorum cluster benchmark

The planned benchmark compares Squirix and etcd on the cost of acknowledging a write on a durable quorum.
Use three nodes per system, matched local storage and network placement, and one replication group per system.

- Measure Squirix SET and etcd PUT from scheduled arrival to acknowledgment. Record the configuration and the guarantees of each acknowledgment.
- Report write p99, completed throughput, queue drops and errors at each offered load.
- Measure quorum reads in a separate workload. Enable quorum reads for Squirix and linearizable reads for etcd.
- Record the environment, client, exact server versions and raw samples so each run can be reproduced.

The runner and first quorum measurements are pending. The results index will list them when available.
