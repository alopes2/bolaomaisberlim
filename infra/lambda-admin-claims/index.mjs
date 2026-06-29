function normalize(email) {
  return email.trim().toLowerCase()
}

export function applyAdminGroup(event, adminEmails) {
  const attributes = event.request?.userAttributes ?? {}
  const groups = event.request?.groupConfiguration?.groupsToOverride ?? []
  const allowlist = new Set(adminEmails.map(normalize))
  const isAdmin = attributes.email_verified === 'true'
    && typeof attributes.email === 'string'
    && allowlist.has(normalize(attributes.email))
  const groupsToOverride = isAdmin
    ? [...new Set([...groups, 'admins'])]
    : groups.filter((group) => group !== 'admins')

  return {
    ...event,
    response: {
      ...(event.response ?? {}),
      claimsOverrideDetails: {
        groupOverrideDetails: {
          groupsToOverride,
          iamRolesToOverride: [],
          preferredRole: null,
        },
      },
    },
  }
}

export async function handler(event) {
  const adminEmails = JSON.parse(process.env.ADMIN_EMAILS ?? '[]')
  return applyAdminGroup(event, adminEmails)
}
