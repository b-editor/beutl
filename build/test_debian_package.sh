#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C DEBIAN_FRONTEND=noninteractive

apt-get update -qq
apt-get install -y --no-install-recommends "$1"

# Read the ELF magic directly so the clean installation needs no extra inspection packages.
checked=0
failed=0
while IFS= read -r -d '' path; do
    magic=$(od -An -tx1 -N4 "$path")
    [[ ${magic// /} == 7f454c46 ]] || continue
    ((++checked))
    # glibc's loader can report an unavailable symbol version and still exit successfully in trace mode.
    if ! dependencies=$(ldd "$path" 2>&1) || [[ $dependencies == *": version "*" not found (required by "* ]]; then
        [[ $dependencies == *"not a dynamic executable"* || $dependencies == *"statically linked"* ]] && continue
        printf '%s\n%s\n' "$path" "$dependencies" >&2
        failed=1
    fi
    while read -r library arrow result; do
        if [[ $arrow == '=>' && $result == 'not found' && $library != 'liblttng-ust.so.0' ]]; then
            printf '%s: missing %s\n' "$path" "$library" >&2
            failed=1
        fi
    done <<< "$dependencies"
done < <(find /usr/lib/beutl -type f -print0)
echo "Checked $checked installed ELF files."
(( checked > 0 && failed == 0 ))
