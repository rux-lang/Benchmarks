# Hardware metadata

Results.json stores Machine.Detected, Machine.Manual and Machine.Effective. Manual Overrides overlay detected fields without destroying their original values. Unknown fields remain absent/null; discovery warnings are preserved.

Windows uses CIM CPU, physical-memory, storage, BIOS and OS information when accessible, runtime CPU-feature probes and powercfg. Linux uses lscpu JSON, /proc/meminfo, /proc/cpuinfo, /etc/os-release, lsblk, findmnt and the selected CPU's scaling governor when exposed. Affinity and exact toolchain identities are recorded on both.

Copy Config/Machine.example.json to Machine.local.json and edit it:

```json
{
  "Label": "Desktop-A",
  "Overrides": {
    "MemorySpeedMTs": 6000,
    "PowerPolicy": "Performance; plugged in"
  },
  "Notes": "CPU 2 is a performance core. Stock limits; air cooling."
}
```

Pass -Machine / --machine on every run that should include those notes.

Useful manual details: RAM speed/timings, physical versus virtual host, power limits, cooling, selected P/E core, BIOS changes, storage/filesystem and background workload. No serial numbers, user account inventory or network configuration are collected.

The first allowed logical CPU is a reproducible selection rule, not a guarantee of the fastest core. Explicitly choose a core when comparing hybrid processors. Do not combine samples from different machines or power policies into one statistical distribution.

