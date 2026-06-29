function normalize(email) {
  return email.trim().toLowerCase()
}

export function applyAdminClaim(event, adminEmails) {
  const attributes = event.request?.userAttributes ?? {}
  const allowlist = new Set(adminEmails.map(normalize))
  const isAdmin = attributes.email_verified === 'true'
    && typeof attributes.email === 'string'
    && allowlist.has(normalize(attributes.email))

  return {
    ...event,
    response: {
      ...(event.response ?? {}),
      claimsAndScopeOverrideDetails: {
        accessTokenGeneration: {
          claimsToAddOrOverride: isAdmin ? { is_admin: 'true' } : {},
        },
      },
    },
  }
}

export async function handler(event) {
  const adminEmails = JSON.parse(process.env.ADMIN_EMAILS ?? '[]')
  return applyAdminClaim(event, adminEmails)
}
