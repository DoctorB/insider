# Security policy

## Project status

Insider is pre-alpha and has no supported release line yet. Security fixes are
made on the `main` branch.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting for this repository when it
is available. If it is not available, contact the repository owner privately and
do not open a public issue containing exploit details.

Include the affected commit, operating system, Unity scripting backend, process
architecture, reproduction steps, and expected impact.

## Plugin trust model

Plugins execute arbitrary code in the game process. Insider does not provide a
sandbox or permission boundary. A malicious plugin has the same operating-system
access as the game process.
