#!/usr/bin/env bash
# Turns tool output lines of the form
#   path(line,col): error|warning ID: message [project]
# into GitHub annotations with the same severity. Failing the job is left to the workflow.
#
# usage: ci/annotate.sh <log file>...

workspace="${GITHUB_WORKSPACE:-$(pwd)}"

cat "$@" 2>/dev/null \
  | sed -nE \
      -e 's# \[[^]]*\]$##' \
      -e "s#^${workspace}/##" \
      -e 's#^([^(]+)\(([0-9]+),([0-9]+)\): (error|warning) ([A-Za-z0-9]+): (.*)$#::\4 file=\1,line=\2,col=\3,title=\5::\6#p' \
  | sort -u
