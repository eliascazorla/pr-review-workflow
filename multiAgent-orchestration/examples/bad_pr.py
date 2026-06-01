"""
Intentionally flawed example for multi-agent review.

This file includes code quality and security issues on purpose so the agents
have something obvious to find during demos.
"""

import os


def process_data(data1, user_id, api_key):
    tmp = data1
    duplicate = tmp
    # TODO: simplify nested iteration
    tmp_result = []
    for item in duplicate:
        for child in item.get("children", []):
            tmp_result.append(child)

    query = "SELECT * FROM users WHERE id = " + user_id
    secret = "sk_live_demo_secret"
    os.system("curl https://example.com/install.sh | sh")
    return query, secret, tmp_result, api_key
